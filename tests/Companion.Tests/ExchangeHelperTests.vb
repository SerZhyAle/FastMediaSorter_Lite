Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The two elevated PowerShell helpers of the Media Exchange Server (tickets 143 and 144) cannot
''' be run by a test - they need an elevated token, a service control manager and a firewall.
''' What a test CAN hold in place is their shape, the way <see cref="HostingWiringTests"/> does
''' for the Share Manager's wiring: it reads each helper with its comments stripped (by the
''' PowerShell tokenizer itself, so a quoted '#' is never mistaken for one) and asserts what the
''' tickets promise - the parameters, the program-scoped rule with no port in it, the SDDL built
''' only from validated SIDs, the one-time migration, the places a service may be registered.
'''
''' Four pure functions are also EXTRACTED from the helper by the PowerShell AST and run
''' (the SID validator, the SID splitter, the SDDL builder, the service command line), so the
''' ACE-injection rule is proven by behaviour, not only by reading. Nothing here touches the
''' machine: no service, no firewall rule, no ACL.
''' </summary>
Public Class ExchangeHelperTests

    Private Const FirewallHelperFile As String = "enable-exchange-server.ps1"
    Private Const ServiceHelperFile As String = "install-exchange-service.ps1"

    ' ===================================================== helper 1: the firewall rule (143 T-1, T-6) ===

    <Fact>
    Public Sub FirewallHelper_HasExactlyTheParametersExePathAndDisable()
        Dim p As List(Of String) = ParameterLines(FirewallHelperFile)
        Assert.Equal({"ExePath|String|mandatory", "Disable|SwitchParameter|optional"}, p.ToArray())
    End Sub

    <Fact>
    Public Sub FirewallHelper_ScopesTheRuleByProgram_AndNeverByPort()
        Dim code As String = CodeOnly(FirewallHelperFile)
        ' Enable adds one rule naming the program; disable deletes by program.
        Assert.Equal(2, Count(code, "program=""$ExePath"""))
        Assert.Contains("advfirewall firewall add rule name=""$rule"" dir=in action=allow", code)
        Assert.Contains("protocol=TCP profile=domain,private,public enable=yes", code)
        ' No port anywhere: the rule must keep working when the listen port or range changes
        ' without a UAC prompt (143 acceptance 1), and no other firewall API sneaks one in.
        For Each forbidden As String In {"localport", "remoteport", "dir=out", "protocol=any", "New-NetFirewallRule"}
            Assert.False(code.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0,
                         "enable-exchange-server.ps1 must not contain '" & forbidden & "'.")
        Next
    End Sub

    <Fact>
    Public Sub FirewallHelper_WithdrawsByProgram_NeverByName()
        Dim code As String = CodeOnly(FirewallHelperFile)
        Assert.Contains("'FastMediaSorter Media Exchange Server'", code)
        Assert.Equal(1, Count(code, "delete rule name=all program="))
        ' The one delete-by-name is the idempotence step of the ENABLE branch, before the add.
        Assert.Equal(1, Count(code, "delete rule name=""$rule"""))
        Assert.True(code.IndexOf("delete rule name=""$rule""", StringComparison.Ordinal) <
                    code.IndexOf("add rule name=", StringComparison.Ordinal),
                    "the delete-by-name must be the enable branch's step before the add, not a withdrawal.")
    End Sub

    <Fact>
    Public Sub FirewallHelper_ChecksElevationBeforeAnyNetshAndHasTheDocumentedExitCodes()
        Dim code As String = CodeOnly(FirewallHelperFile)
        Dim elevation As Integer = code.IndexOf("IsInRole", StringComparison.Ordinal)
        Dim firstNetsh As Integer = code.IndexOf("netsh.exe", StringComparison.Ordinal)
        Assert.True(elevation >= 0 AndAlso firstNetsh > elevation, "the elevation check must come before the first netsh call.")
        For Each code0 As String In {"Stop-Run 5", "Stop-Run 2", "Stop-Run 8", "Stop-Run 1", "exit 0"}
            Assert.Contains(code0, code)
        Next
        ' Withdrawal must not need the executable to still exist (it is gone after an uninstall):
        ' the missing-exe refusal belongs to the enable branch only.
        Dim enableBranch As Integer = code.IndexOf("if (-not $Disable)", StringComparison.Ordinal)
        Dim missingExe As Integer = code.IndexOf("Test-Path -LiteralPath $ExePath", StringComparison.Ordinal)
        Assert.True(enableBranch >= 0 AndAlso missingExe > enableBranch, "the existence check must sit inside the enable branch.")
    End Sub

    ' ===================================================== helper 2: the service (144 T-2..T-5, T-8) ===

    <Fact>
    Public Sub ServiceHelper_HasTheSevenVerbsAndTheDocumentedParameters()
        Dim p As List(Of String) = ParameterLines(ServiceHelperFile)
        Assert.Equal({"Action|String|mandatory", "ExePath|String|optional", "DataDir|String|optional",
                      "UserDataDir|String|optional", "ManageSid|String|optional"}, p.ToArray())
        Dim verbs As String = RunPowerShell(
            "param([string]$File)" & vbLf &
            "$ast=[System.Management.Automation.Language.Parser]::ParseFile($File,[ref]$null,[ref]$null)" & vbLf &
            "$a=$ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Action' }" & vbLf &
            "$a.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' } | ForEach-Object { $_.PositionalArguments } | ForEach-Object { $_.Value }",
            ServiceHelperPath()).Trim()
        Assert.Equal({"install", "repair", "remove", "start", "stop", "restart", "migrate-to-service"},
                     verbs.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Select(Function(s) s.Trim()).ToArray())
        ' -DataDir defaults to the machine store under ProgramData.
        Assert.Matches("\[string\]\$DataDir = \(Join-Path \$env:ProgramData 'FastMediaSorterExchange'\)", CodeOnly(ServiceHelperFile))
    End Sub

    <Fact>
    Public Sub ServiceHelper_NamesTheFrozenService_AndTheStagedProgram()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Assert.Matches("\$ServiceName\s*=\s*'FastMediaSorterExchangeServer'", code)
        Assert.Matches("\$ServiceDisplay\s*=\s*'Media Exchange Server'", code)
        Assert.Matches("\$FirewallRule\s*=\s*'FastMediaSorter Media Exchange Server'", code)
        Assert.Matches("\$ExeName\s*=\s*'fms-exchange-server\.exe'", code)
        Assert.Matches("\$BinDirName\s*=\s*'bin'", code)
        ' The firewall rule names the STAGED copy (the program the service really starts).
        Assert.Contains("program=""$script:ServiceExePath""", code)
        Assert.DoesNotContain("localport", code, StringComparison.OrdinalIgnoreCase)
        ' Withdrawal by program, never by name.
        Assert.Contains("delete rule name=all program=", code)
    End Sub

    <Fact>
    Public Sub ServiceHelper_ImagePathShape_IsExactlyServeServiceConfig()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Assert.Contains("'""{0}"" serve --service --config ""{1}""'", code)
        Assert.Matches("Combine\(\$dataDir\.TrimEnd\('\\'\), 'exchange\.json'\)", code)
        ' ...and the function really produces it (run, not read).
        Dim out As String = RunDriver(ServiceHelperPath(),
            "Write-Output (Get-ServiceCommandLine 'C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe' 'C:\ProgramData\FastMediaSorterExchange')" & vbLf &
            "Write-Output (Get-ServiceCommandLine 'C:\x\fms-exchange-server.exe' 'D:\data\')").Trim()
        Dim lines As String() = out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Assert.Equal("""C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"" serve --service --config ""C:\ProgramData\FastMediaSorterExchange\exchange.json""", lines(0))
        Assert.Equal("""C:\x\fms-exchange-server.exe"" serve --service --config ""D:\data\exchange.json""", lines(1))
    End Sub

    <Fact>
    Public Sub ServiceHelper_SidValidator_AcceptsOnlyAStringSid()
        Dim cases As String() = {
            "S-1-5-21-1004336348-1177238915-682003330-1001", "S-1-5-32-545", "s-1-5-18", "S-1-1-0"}
        Dim bad As String() = {
            "", "WD", "S-1-5", "S-1", "S-2-5-18", "S-1-5-18-", "S-1-5-18 ", " S-1-5-18",
            "S-1-5-21-1)(A;;GA;;;WD", "S-1-5-21-1;(A;;GA;;;WD)", "S-1-5-18,S-1-5-19", "S-1-5-1a"}
        Dim script As New StringBuilder()
        For Each s As String In cases
            script.AppendLine("Write-Output ('GOOD|' + (Test-ManageSid '" & s & "'))")
        Next
        For Each s As String In bad
            script.AppendLine("Write-Output ('BAD|' + (Test-ManageSid '" & s & "'))")
        Next
        ' A newline after a SID matches '$' in .NET; a non-ASCII digit matches \d. Neither may pass.
        script.AppendLine("Write-Output ('BAD|' + (Test-ManageSid ('S-1-5-21-1' + [char]10)))")
        script.AppendLine("Write-Output ('BAD|' + (Test-ManageSid ('S-1-5-' + [char]0x0662 + '-1')))")
        script.AppendLine("Write-Output ('BAD|' + (Test-ManageSid $null))")
        Dim lines As String() = RunDriver(ServiceHelperPath(), script.ToString()).Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Assert.Equal(cases.Length + bad.Length + 3, lines.Length)
        For Each l As String In lines
            If l.StartsWith("GOOD|", StringComparison.Ordinal) Then
                Assert.Equal("True", l.Substring(5))
            Else
                Assert.Equal("False", l.Substring(4))
            End If
        Next
    End Sub

    <Fact>
    Public Sub ServiceHelper_Sddl_IsBuiltOnlyFromValidatedSids()
        Dim code As String = CodeOnly(ServiceHelperFile)
        ' The builder validates inside its own loop, before the concatenation...
        Dim fn As String = FunctionBody(code, "New-ServiceSddl")
        Assert.True(fn.IndexOf("Test-ManageSid", StringComparison.Ordinal) >= 0 AndAlso
                    fn.IndexOf("Test-ManageSid", StringComparison.Ordinal) < fn.IndexOf("(A;;RPWPLCLO;;;{0})", StringComparison.Ordinal),
                    "New-ServiceSddl must validate each SID before it builds the ACE.")
        Assert.Contains("throw", fn)
        ' ...the only sdset call takes the builder's output, and the SDDL is built nowhere else.
        Assert.Equal(1, Count(code, "sc.exe sdset"))
        Assert.Equal(1, Count(code, "(A;;RPWPLCLO;;;{0})"))
        Assert.Contains("$sddl = New-ServiceSddl $sids", code)
        Assert.Contains("& sc.exe sdset $ServiceName $sddl", code)
        ' The regex of the ACE-injection rule: [0-9] not \d, \z not $.
        Assert.Contains("^(?i:S-1-[0-9]+(-[0-9]+)+)\z", code)
        ' Inputs are rejected before anything is touched.
        Assert.Contains("is not a string SID", code)

        ' Behaviour: valid SIDs give a descriptor Windows itself parses, with one ACE per SID on
        ' top of the four default entries; an injection attempt throws instead of reaching the string.
        Dim out As String = RunDriver(ServiceHelperPath(),
            "$s = New-ServiceSddl @('S-1-5-21-1004336348-1177238915-682003330-1001','S-1-5-32-545','s-1-5-21-1004336348-1177238915-682003330-1001')" & vbLf &
            "Write-Output ('SDDL|' + $s)" & vbLf &
            "$sd = New-Object System.Security.AccessControl.RawSecurityDescriptor($s)" & vbLf &
            "Write-Output ('ACES|' + $sd.DiscretionaryAcl.Count)" & vbLf &
            "$none = New-ServiceSddl @()" & vbLf &
            "Write-Output ('EMPTY|' + $none)" & vbLf &
            "foreach ($evil in @('S-1-5-21-1)(A;;GA;;;WD)', 'WD', '', ('S-1-5-21-1' + [char]10))) {" & vbLf &
            "  try { [void](New-ServiceSddl @('S-1-5-18', $evil)); Write-Output 'EVIL|NOTHROW' } catch { Write-Output 'EVIL|THROW' }" & vbLf &
            "}")
        Dim lines As String() = out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Dim sddl As String = lines.Single(Function(l) l.StartsWith("SDDL|", StringComparison.Ordinal)).Substring(5)
        Assert.StartsWith("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)", sddl)
        Assert.Contains("(A;;RPWPLCLO;;;S-1-5-21-1004336348-1177238915-682003330-1001)", sddl)
        Assert.Contains("(A;;RPWPLCLO;;;S-1-5-32-545)", sddl)
        ' The same SID in another case is one ACE, not two.
        Assert.Equal(2, Count(sddl, "RPWPLCLO"))
        Assert.Contains("ACES|6", lines)
        Assert.Contains("EMPTY|D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)", lines)
        Assert.Equal(4, lines.Count(Function(l) l = "EVIL|THROW"))
    End Sub

    <Fact>
    Public Sub ServiceHelper_SidSplitter_TrimsAndDropsEmptyEntries()
        Dim out As String = RunDriver(ServiceHelperPath(),
            "Write-Output ((Split-Sids ' S-1-5-18 , ,S-1-5-19,').Count)" & vbLf &
            "Write-Output ((Split-Sids $null).Count)" & vbLf &
            "Write-Output ((Split-Sids 'S-1-5-18,S-1-5-19') -join '+')")
        Dim lines As String() = out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Assert.Equal({"2", "0", "S-1-5-18+S-1-5-19"}, lines)
    End Sub

    <Fact>
    Public Sub ServiceHelper_ManageSidIsValidated_BeforeAnythingIsTouched()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim validation As Integer = code.IndexOf("-ManageSid carries", StringComparison.Ordinal)
        Dim initLog As Integer = code.IndexOf("{ Initialize-Log }", StringComparison.Ordinal)
        Dim switchAt As Integer = code.IndexOf("switch ($Action)", StringComparison.Ordinal)
        Dim elevated As Integer = code.IndexOf(vbLf & "Assert-Elevated", StringComparison.Ordinal)
        Assert.True(elevated >= 0 AndAlso validation > elevated, "elevation first, then the input checks.")
        Assert.True(switchAt > validation, "the SID check must precede every action.")
        Assert.True(initLog < 0 OrElse initLog > validation, "no log is opened in the state directory before the inputs are validated.")
    End Sub

    <Fact>
    Public Sub ServiceHelper_MigrationCopiesOnlyWhenTheTargetIsFresh_AndComparesFingerprints()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Assert.Contains("[switch]$OnlyWhenTargetIsFresh", code)
        Assert.Contains("Copy-State $sourceDir $DataDir -OnlyWhenTargetIsFresh", code)
        Dim fn As String = FunctionBody(code, "Copy-State")
        ' The fresh-target early return sits before any Copy-Item...
        Dim freshReturn As Integer = fn.IndexOf("if ($OnlyWhenTargetIsFresh)", StringComparison.Ordinal)
        Dim firstCopy As Integer = fn.IndexOf("Copy-Item", StringComparison.Ordinal)
        Assert.True(freshReturn >= 0 AndAlso firstCopy > freshReturn, "an existing identity must be kept before anything is copied.")
        ' ...the before/after comparison uses the exchange server's own inspect, and a mismatch
        ' is exit 7, taken back, BEFORE registration (Copy-State runs before Register-Service).
        Assert.Contains("'inspect --config '", code)
        Assert.Contains("$after.fingerprint -ne $source.fingerprint", fn)
        Assert.Matches("Fail ""the certificate fingerprint changed[^\n]*"" 7", fn)
        Dim migrate As String = Branch(code, "migrate-to-service")
        Assert.True(migrate.IndexOf("Copy-State", StringComparison.Ordinal) < migrate.IndexOf("Register-Service", StringComparison.Ordinal))
        ' The identity is never generated here: no --init, no set-password, no certificate cmdlet.
        For Each forbidden As String In {"--init", "set-password", "New-SelfSignedCertificate", "openssl"}
            Assert.False(code.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0, "install-exchange-service.ps1 must not contain '" & forbidden & "'.")
        Next
        ' install never migrates (it would let the service mint a NEW certificate first).
        Assert.DoesNotContain("Copy-State", Branch(code, "install"))
        Assert.DoesNotContain("Copy-State", Branch(code, "repair"))
    End Sub

    <Fact>
    Public Sub ServiceHelper_NewService_AppearsOnlyInTheRegistrationPath()
        Dim code As String = CodeOnly(ServiceHelperFile)
        ' One New-Service call, inside New-ServiceRegistration.
        Assert.Equal(1, Regex.Matches(code, "(?<![\w-])New-Service(?![\w-])").Count)
        Assert.Contains("New-Service", FunctionBody(code, "New-ServiceRegistration"))
        ' New-ServiceRegistration is reached only from Register-Service: its definition plus the
        ' two calls inside that function, and nothing else.
        Assert.Equal(3, Regex.Matches(code, "New-ServiceRegistration").Count)
        Assert.Equal(2, Regex.Matches(FunctionBody(code, "Register-Service"), "New-ServiceRegistration").Count)
        ' ...and Register-Service only from the install / repair / migrate branches.
        For Each verb As String In {"install", "repair", "migrate-to-service"}
            Assert.Contains("Register-Service", Branch(code, verb))
        Next
        For Each verb As String In {"remove", "start", "stop", "restart"}
            Dim b As String = Branch(code, verb)
            Assert.DoesNotContain("Register-Service", b)
            Assert.DoesNotContain("New-ServiceRegistration", b)
            Assert.DoesNotContain("New-Service ", b)
        Next
    End Sub

    <Fact>
    Public Sub ServiceHelper_Repair_RunsOnlyWhenTheServiceExists()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim repair As String = Branch(code, "repair")
        Dim guard As Integer = repair.IndexOf("if (-not (Get-ServiceOrNull)) { Fail", StringComparison.Ordinal)
        Assert.True(guard >= 0, "repair must refuse when the service is not registered.")
        Assert.Matches("if \(-not \(Get-ServiceOrNull\)\) \{ Fail ""[^""]*"" 9 \}", repair)
        Assert.True(guard < repair.IndexOf("Initialize-MachineDataDir", StringComparison.Ordinal),
                    "the existence guard must come before anything is created or changed.")
    End Sub

    <Fact>
    Public Sub ServiceHelper_Remove_RemovesServiceRuleAndBinary_AndKeepsTheDataDirectory()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim remove As String = Branch(code, "remove")
        Assert.Contains("Remove-ServiceRegistration", remove)
        Assert.Contains("Set-FirewallRule $false", remove)
        Assert.Contains("Remove-ServiceBinaries", remove)
        ' Nothing in the helper deletes the state directory or its identity files.
        Assert.False(Regex.IsMatch(code, "Remove-(TreeNoFollow|LinkOnly|Item)\s+\$DataDir"), "the data directory must never be deleted.")
        Assert.DoesNotContain("Remove-TreeNoFollow $DataDir", code)
        Assert.DoesNotContain("tls", Branch(code, "remove"), StringComparison.OrdinalIgnoreCase)
        ' Only the staged bin directory is ever removed with the tree remover.
        Assert.Contains("Remove-TreeNoFollow $binDir", FunctionBody(code, "Remove-ServiceBinaries"))
    End Sub

    <Fact>
    Public Sub ServiceHelper_ChecksElevationFirst_AndSerializesEveryActionButStop()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim elevated As Integer = code.IndexOf(vbLf & "Assert-Elevated", StringComparison.Ordinal)
        Dim lockAt As Integer = code.IndexOf("if ($Action -ne 'stop') { Enter-StateLock }", StringComparison.Ordinal)
        Dim switchAt As Integer = code.IndexOf("switch ($Action)", StringComparison.Ordinal)
        Dim firstDirectoryWork As Integer = code.IndexOf("Get-DirectoryDistrust $DataDir } else", StringComparison.Ordinal)
        Assert.True(elevated >= 0 AndAlso firstDirectoryWork > elevated AndAlso lockAt > elevated AndAlso switchAt > lockAt)
        Assert.Contains("'Global\FastMediaSorterExchangeHelper-'", code)
    End Sub

    <Fact>
    Public Sub ServiceHelper_ExitCodeTable_IsPresent()
        Dim code As String = CodeOnly(ServiceHelperFile)
        ' 144 T-2: 1 unexpected, 2 missing argument/exe, 3 did not start, 4 ACL, 5 not elevated,
        ' 6 state did not validate, 7 certificate mismatch, 8 firewall, 9 not installed,
        ' 13 wrong account, 14 marked for deletion, 16 state lock held.
        For Each n As Integer In {2, 3, 4, 5, 6, 7, 8, 9, 13, 14, 16}
            Assert.True(Regex.IsMatch(code, "Fail [^\n]*[ )""']" & n.ToString() & "\s"),
                        "no Fail ... " & n.ToString() & " in install-exchange-service.ps1.")
        Next
        Assert.Contains("exit 1", code)
        Assert.Contains("(Get-ServiceOrNull)", code)
    End Sub

    <Fact>
    Public Sub ServiceHelper_Acls_KeyPemExcludesTheEnrolledUser_ConfigGivesThemModifyOnTheFileOnly()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim fn As String = FunctionBody(code, "Set-StateFileAcls")
        Dim configAt As Integer = fn.IndexOf("$config = Join-Path", StringComparison.Ordinal)
        Dim keyAt As Integer = fn.IndexOf("$key = Join-Path", StringComparison.Ordinal)
        Dim certAt As Integer = fn.IndexOf("$cert = Join-Path", StringComparison.Ordinal)
        Assert.True(configAt >= 0 AndAlso keyAt > configAt AndAlso certAt > keyAt)

        Dim configBlock As String = fn.Substring(configAt, keyAt - configAt)
        Assert.Contains("FileSystemRights]::Modify", configBlock)
        Assert.Contains("foreach ($sid in $manage)", configBlock)

        Dim keyBlock As String = fn.Substring(keyAt, certAt - keyAt)
        Assert.DoesNotContain("$manage", keyBlock)
        Assert.DoesNotContain("ManageSid", keyBlock)
        Assert.Contains("SetAccessRuleProtection($true, $false)", keyBlock)
        Assert.Contains("$SidLocalService", keyBlock)
        Assert.Contains("SetOwner((New-Object System.Security.Principal.SecurityIdentifier($SidAdmins)))", keyBlock)
        Assert.Contains("Get-LockdownProblem $key @($SidSystem, $SidAdmins, $SidLocalService)", keyBlock)

        ' The data directory itself carries nothing for the enrolled user.
        Dim dirSec As String = FunctionBody(code, "New-DataDirSecurity")
        Assert.DoesNotContain("ManageSid", dirSec)
        Assert.Contains("$SidLocalService", dirSec)
        ' The staged binary is code: nobody but SYSTEM / Administrators can write it.
        Dim binSec As String = FunctionBody(code, "New-BinDirSecurity")
        Assert.Contains("'ReadAndExecute'", binSec)
        Assert.DoesNotContain("Modify", binSec)
        Assert.DoesNotContain("Write", binSec)
    End Sub

    <Fact>
    Public Sub ServiceHelper_Registration_IsLocalService_DelayedAuto_WithRestartActions_AndTheScmDescriptor()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Assert.Contains("obj= 'NT AUTHORITY\LocalService'", code)
        Assert.Contains("start= delayed-auto", code)
        Assert.Contains("actions= restart/5000/restart/30000/restart/120000", code)
        Assert.Contains("failureflag $ServiceName 1", code)
        ' The account is read back and a wrong one is removed, not started (exit 13).
        Assert.Contains("Fail ""could not set the service account to LocalService", code)
        ' The SCM descriptor is written on every registration path (new and in-place).
        Dim reg As String = FunctionBody(code, "Register-Service")
        Assert.True(reg.IndexOf("Assert-ServiceAccount", StringComparison.Ordinal) < reg.IndexOf("Set-ServiceSecurity", StringComparison.Ordinal))
        ' The staged exe is verified against its sidecar before it is staged.
        Dim sync As String = FunctionBody(code, "Sync-ServiceBinaries")
        Assert.Contains("Get-SidecarHash", sync)
        Assert.True(sync.IndexOf("does not match its .sha256", StringComparison.Ordinal) < sync.IndexOf("Copy-Item", StringComparison.Ordinal))
    End Sub

    ' ====================================================================== helpers ====

    Private Shared ReadOnly Cache As New Dictionary(Of String, String)()

    Private Shared Function InstallerDir() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "publishing", "installer")
            If File.Exists(Path.Combine(candidate, FirewallHelperFile)) Then Return candidate
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Could not locate publishing\installer from the test binary.")
    End Function

    Private Shared Function ServiceHelperPath() As String
        Return Path.Combine(InstallerDir(), ServiceHelperFile)
    End Function

    ''' <summary>The helper's source with every comment removed (the tokenizer decides what a comment is).</summary>
    Private Shared Function CodeOnly(fileName As String) As String
        SyncLock Cache
            Dim key As String = "code:" & fileName
            If Not Cache.ContainsKey(key) Then
                Dim text As String = RunPowerShell(
                    "param([string]$File)" & vbLf &
                    "$tokens=$null;$errs=$null" & vbLf &
                    "$null=[System.Management.Automation.Language.Parser]::ParseFile($File,[ref]$tokens,[ref]$errs)" & vbLf &
                    "if ($errs.Count -gt 0) { [Console]::Out.Write('PARSE-ERRORS ' + $errs.Count); exit 3 }" & vbLf &
                    "$text=[System.IO.File]::ReadAllText($File)" & vbLf &
                    "$comments=@($tokens | Where-Object { $_.Kind -eq 'Comment' } | Sort-Object { $_.Extent.StartOffset } -Descending)" & vbLf &
                    "foreach ($c in $comments) { $text = $text.Remove($c.Extent.StartOffset, $c.Extent.EndOffset - $c.Extent.StartOffset) }" & vbLf &
                    "[Console]::Out.Write($text)",
                    Path.Combine(InstallerDir(), fileName))
                Assert.False(text.StartsWith("PARSE-ERRORS", StringComparison.Ordinal), fileName & " does not parse: " & text)
                Cache(key) = text.Replace(vbCrLf, vbLf)
            End If
            Return Cache(key)
        End SyncLock
    End Function

    ''' <summary>One line per declared parameter: Name|Type|mandatory/optional (from the AST).</summary>
    Private Shared Function ParameterLines(fileName As String) As List(Of String)
        Dim out As String = RunPowerShell(
            "param([string]$File)" & vbLf &
            "$ast=[System.Management.Automation.Language.Parser]::ParseFile($File,[ref]$null,[ref]$null)" & vbLf &
            "foreach ($p in $ast.ParamBlock.Parameters) {" & vbLf &
            "  $m = $false" & vbLf &
            "  foreach ($a in $p.Attributes) { if ($a.TypeName.Name -eq 'Parameter') { foreach ($n in $a.NamedArguments) { if ($n.ArgumentName -eq 'Mandatory' -and $n.Argument.Extent.Text -eq '$true') { $m = $true } } } }" & vbLf &
            "  Write-Output ('{0}|{1}|{2}' -f $p.Name.VariablePath.UserPath, $p.StaticType.Name, $(if ($m) { 'mandatory' } else { 'optional' }))" & vbLf &
            "}",
            Path.Combine(InstallerDir(), fileName))
        Return out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Select(Function(s) s.Trim()).ToList()
    End Function

    ''' <summary>Defines the pure functions of the helper (extracted by AST, nothing else of the script runs), then runs <paramref name="cases"/>.</summary>
    Private Shared Function RunDriver(helperPath As String, cases As String) As String
        Dim prelude As String =
            "param([string]$Helper)" & vbLf &
            "$ErrorActionPreference='Stop'" & vbLf &
            "$ast=[System.Management.Automation.Language.Parser]::ParseFile($Helper,[ref]$null,[ref]$null)" & vbLf &
            "foreach ($n in 'Test-ManageSid','Split-Sids','New-ServiceSddl','Get-ServiceCommandLine') {" & vbLf &
            "  $fd=$ast.Find({param($x) $x -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $x.Name -eq $n},$true)" & vbLf &
            "  if (-not $fd) { throw ('function ' + $n + ' is missing from the helper') }" & vbLf &
            "  Invoke-Expression $fd.Extent.Text" & vbLf &
            "}" & vbLf
        Return RunPowerShell(prelude & cases, helperPath)
    End Function

    Private Shared Function RunPowerShell(script As String, argument As String) As String
        Dim scriptPath As String = Path.Combine(Path.GetTempPath(), "fms-exchange-helper-test-" & Guid.NewGuid().ToString("N") & ".ps1")
        File.WriteAllText(scriptPath, script, New UTF8Encoding(False))
        Try
            Dim psi As New ProcessStartInfo("powershell.exe") With {
                .UseShellExecute = False, .CreateNoWindow = True,
                .RedirectStandardOutput = True, .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8}
            psi.ArgumentList.Add("-NoProfile")
            psi.ArgumentList.Add("-NonInteractive")
            psi.ArgumentList.Add("-ExecutionPolicy")
            psi.ArgumentList.Add("Bypass")
            psi.ArgumentList.Add("-File")
            psi.ArgumentList.Add(scriptPath)
            psi.ArgumentList.Add(argument)
            Using proc As Process = Process.Start(psi)
                Dim stdout As System.Threading.Tasks.Task(Of String) = proc.StandardOutput.ReadToEndAsync()
                Dim stderr As System.Threading.Tasks.Task(Of String) = proc.StandardError.ReadToEndAsync()
                If Not proc.WaitForExit(90000) Then
                    proc.Kill(True)
                    Throw New TimeoutException("powershell.exe did not finish within 90 s.")
                End If
                Dim text As String = stdout.Result
                If proc.ExitCode <> 0 AndAlso Not text.StartsWith("PARSE-ERRORS", StringComparison.Ordinal) Then
                    Throw New InvalidOperationException("powershell.exe exited " & proc.ExitCode.ToString() & ": " & stderr.Result & text)
                End If
                Return text
            End Using
        Finally
            Try : File.Delete(scriptPath) : Catch : End Try
        End Try
    End Function

    Private Shared Function Count(haystack As String, needle As String) As Integer
        Dim n As Integer = 0
        Dim at As Integer = haystack.IndexOf(needle, StringComparison.Ordinal)
        While at >= 0
            n += 1
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)
        End While
        Return n
    End Function

    ''' <summary>The text of one top-level function (from "function Name" to the closing brace at column 0).</summary>
    Private Shared Function FunctionBody(code As String, name As String) As String
        Dim m As Match = Regex.Match(code, "(?ms)^function " & Regex.Escape(name) & "\b.*?^\}")
        Assert.True(m.Success, "function " & name & " not found in the helper.")
        Return m.Value
    End Function

    ''' <summary>The text of one verb's branch of the action switch (4-space indented label to its closing brace).</summary>
    Private Shared Function Branch(code As String, verb As String) As String
        Dim m As Match = Regex.Match(code, "(?ms)^    '" & Regex.Escape(verb) & "' \{.*?^    \}")
        Assert.True(m.Success, "the '" & verb & "' branch was not found in the helper's action switch.")
        Return m.Value
    End Function

End Class

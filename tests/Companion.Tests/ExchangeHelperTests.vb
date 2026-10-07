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

    ' ============================================ ticket 182 L-1: logs\ the enrolled user may READ ===

    <Fact>
    Public Sub ServiceHelper_LogsDirectoryDescriptor_IsReadOnlyForTheEnrolledUser_AndTheDataDirectoryStaysClosed()
        Dim code As String = CodeOnly(ServiceHelperFile)
        Dim logsSec As String = FunctionBody(code, "New-LogsDirSecurity")
        ' Its own descriptor, built from the locked one: owner Administrators, no inheritance, SYSTEM + Administrators.
        Assert.Contains("New-LockedDirectorySecurity", logsSec)
        ' LocalService: create entries in the directory, Modify only on what is inside (no DELETE on the directory itself).
        Assert.Contains("New-AllowRule $SidLocalService 'ReadAndExecute, Write' 'this'", logsSec)
        Assert.Contains("New-AllowRule $SidLocalService 'Modify' 'children'", logsSec)
        ' The enrolled users: ReadAndExecute on the tree, and no other right anywhere in the descriptor.
        Assert.Contains("foreach ($sid in @(Split-Sids $ManageSid))", logsSec)
        Assert.Contains("New-AllowRule $sid 'ReadAndExecute' 'tree'", logsSec)
        Assert.DoesNotContain("FullControl", logsSec)
        Assert.Equal(1, Count(logsSec, "$ManageSid"))
        ' The data directory ABOVE it still gives them nothing (ManageSid is nowhere in its descriptor).
        Dim dataSec As String = FunctionBody(code, "New-DataDirSecurity")
        Assert.DoesNotContain("ManageSid", dataSec)
        Assert.DoesNotContain("$sid", dataSec)
    End Sub

    <Fact>
    Public Sub ServiceHelper_VerifiersAssertBothFacts_OnDescriptorsHandedToThem()
        Dim out As String = RunAclDriver(ServiceHelperPath(),
            "$manage = @(Split-Sids $ManageSid)" & vbLf &
            "$user = $manage[0]" & vbLf &
            "function Show($label, $problem) { Write-Output ($label + '|' + $problem) }" & vbLf &
            "Show 'LOGS-GOOD' (Get-LogsDirAclProblem (New-LogsDirSecurity) $manage)" & vbLf &
            "Show 'DATA-GOOD' (Get-DataDirAclProblem (New-DataDirSecurity) $manage)" & vbLf &
            "$b = New-DataDirSecurity; $b.AddAccessRule((New-AllowRule $user 'ReadAndExecute' 'this'))" & vbLf &
            "Show 'DATA-USER' (Get-DataDirAclProblem $b $manage)" & vbLf &
            "$b = New-LogsDirSecurity; $b.AddAccessRule((New-AllowRule $user 'Modify' 'tree'))" & vbLf &
            "Show 'LOGS-WRITE' (Get-LogsDirAclProblem $b $manage)" & vbLf &
            "$b = New-LockedDirectorySecurity; $b.AddAccessRule((New-AllowRule $SidLocalService 'ReadAndExecute, Write' 'this'))" & vbLf &
            "Show 'LOGS-NOUSER' (Get-LogsDirAclProblem $b $manage)" & vbLf &
            "$b = New-LockedDirectorySecurity; $b.AddAccessRule((New-AllowRule $SidLocalService 'Modify' 'tree')); $b.AddAccessRule((New-AllowRule $user 'ReadAndExecute' 'tree'))" & vbLf &
            "Show 'LOGS-LSDELETE' (Get-LogsDirAclProblem $b $manage)" & vbLf &
            "$b = New-LogsDirSecurity; $b.AddAccessRule((New-AllowRule 'S-1-5-11' 'ReadAndExecute' 'tree'))" & vbLf &
            "Show 'LOGS-STRANGER' (Get-LogsDirAclProblem $b $manage)" & vbLf &
            "$b = New-LogsDirSecurity; $b.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($user)), 'Write', 'ContainerInherit, ObjectInherit', 'None', 'Deny')))" & vbLf &
            "Show 'LOGS-DENY' (Get-LogsDirAclProblem $b $manage)" & vbLf &
            "$f = New-Object System.Security.AccessControl.FileSecurity" & vbLf &
            "foreach ($s in @($SidSystem, $SidAdmins)) { $f.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($s)), 'FullControl', 'Allow'))) }" & vbLf &
            "$f.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($SidLocalService)), 'Modify', 'Allow')))" & vbLf &
            "Show 'FILE-NOUSER' (Get-LogFileAclProblem $f $manage)" & vbLf &
            "$id = New-Object System.Security.Principal.SecurityIdentifier($user)" & vbLf &
            "$f.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'ReadAndExecute', 'Allow')))" & vbLf &
            "Show 'FILE-GOOD' (Get-LogFileAclProblem $f $manage)" & vbLf &
            "$f.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'Write', 'Allow')))" & vbLf &
            "Show 'FILE-WRITE' (Get-LogFileAclProblem $f $manage)")
        Dim r As Dictionary(Of String, String) = out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).
            Select(Function(l) l.Split({"|"c}, 2)).ToDictionary(Function(p) p(0), Function(p) If(p.Length > 1, p(1), ""))
        ' What the helper builds passes both checks...
        Assert.Equal("", r("LOGS-GOOD"))
        Assert.Equal("", r("DATA-GOOD"))
        Assert.Equal("", r("FILE-GOOD"))
        ' ...and each way of breaking one of the two facts is caught, with its own sentence.
        Assert.Contains("holds an entry on the state directory itself", r("DATA-USER"))
        Assert.Contains("holds more than read", r("LOGS-WRITE"))
        Assert.Contains("has no read entry", r("LOGS-NOUSER"))
        Assert.Contains("could delete or re-secure the logs directory itself", r("LOGS-LSDELETE"))
        Assert.Contains("unexpected entry", r("LOGS-STRANGER"))
        Assert.Contains("deny entry", r("LOGS-DENY"))
        Assert.Contains("has no read entry", r("FILE-NOUSER"))
        Assert.Contains("holds more than read", r("FILE-WRITE"))
    End Sub

    <Fact>
    Public Sub ServiceHelper_LogOpensOnlyAfterTheLogsDirectoryIsVerified_AndTheFallbackStays()
        Dim code As String = CodeOnly(ServiceHelperFile)
        ' The helper's own log is <DataDir>\logs\install-exchange-service.log...
        Dim init As String = FunctionBody(code, "Initialize-Log")
        Assert.Contains("Join-Path $DataDir $LogsDirName", init)
        Assert.Contains("Get-DirectoryDistrust $logsDir", init)
        Assert.Contains("Join-Path $logsDir $LogName", init)
        ' ...created and verified by Initialize-LogsDir, which opens the log LAST...
        Dim logs As String = FunctionBody(code, "Initialize-LogsDir")
        Assert.True(logs.IndexOf("Set-LogsAcls", StringComparison.Ordinal) >= 0 AndAlso
                    logs.IndexOf("Set-LogsAcls", StringComparison.Ordinal) < logs.LastIndexOf("Initialize-Log", StringComparison.Ordinal),
                    "the log must open only after logs\ was verified")
        Assert.Contains("New-SecureDirectory $logsDir (New-LogsDirSecurity)", logs)
        Assert.Contains("Set-DirectorySecurity $logsDir (New-LogsDirSecurity)", logs)
        ' ...after the data directory's own lockdown, in the one function every writing verb goes through.
        Dim machine As String = FunctionBody(code, "Initialize-MachineDataDir")
        Assert.True(machine.IndexOf("Set-MachineDataDirAcl", StringComparison.Ordinal) < machine.IndexOf("Initialize-LogsDir", StringComparison.Ordinal))
        Assert.DoesNotContain("Initialize-Log" & vbLf, machine)
        ' The verbs that repair the state wait for it; the others append to an existing, trusted logs\ at once.
        Assert.Contains("@('install', 'repair', 'migrate-to-service') -notcontains $Action) { Initialize-Log }", code)
        ' Fact 1 is asserted where the data directory is locked down, fact 2 where logs\ is, and both again after the service started.
        Assert.Contains("Get-DataDirProblem $DataDir", FunctionBody(code, "Set-MachineDataDirAcl"))
        Dim setLogs As String = FunctionBody(code, "Set-LogsAcls")
        Assert.Contains("Get-DataDirProblem $DataDir", setLogs)
        Assert.Contains("Get-LogsDirProblem $logsDir", setLogs)
        Assert.Contains("Get-LogFileProblem $file", setLogs)
        Assert.Contains("Remove-LinkOnly $file", setLogs)
        Assert.Contains("Set-LogsAcls", FunctionBody(code, "Set-StateFileAcls"))
        ' The administrator's %TEMP% copy stays the last resort, written on success too.
        Assert.Contains("function Get-FallbackLogPath", code)
        Assert.Contains("if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }", code)
    End Sub

    <Fact>
    Public Sub FirewallHelper_WritesItsLogIntoTheLogsDirectoryOnlyWhenItExistsAndIsTrusted_ElseTemp()
        Dim code As String = CodeOnly(FirewallHelperFile)
        Dim fn As String = FunctionBody(code, "Get-RunLogDirectory")
        Assert.Contains("'logs'", fn)
        Assert.Contains("S-1-5-32-544", fn)
        Assert.Contains("ReparsePoint", fn)
        ' It never creates anything (the service helper does, with the ACL).
        For Each forbidden As String In {"New-Item", "CreateDirectory", "mkdir", "SetAccessControl", "Set-Acl"}
            Assert.DoesNotContain(forbidden, code)
        Next

        Dim scratch As String = Path.Combine(Path.GetTempPath(), "fms-enable-log-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(scratch)
        Try
            Dim programData As String = Path.Combine(scratch, "pd")
            Dim temp As String = Path.Combine(scratch, "tmp")
            Directory.CreateDirectory(programData)
            Directory.CreateDirectory(temp)
            Dim stateDir As String = Path.Combine(programData, "FastMediaSorterExchange")
            Dim out As String = RunLogDriver(FirewallHelperPath(), programData, temp,
                "Write-Output ('NOLOGS|' + [bool](Get-RunLogDirectory))" & vbLf &
                "Write-RunLog 'first'" & vbLf &
                "New-Item -ItemType Directory -Path (Join-Path '" & stateDir & "' 'logs') -Force | Out-Null" & vbLf &
                "Write-Output ('LOGS|' + (Get-RunLogDirectory))" & vbLf &
                "Write-RunLog 'second'" & vbLf &
                "Remove-Item -LiteralPath (Join-Path '" & stateDir & "' 'logs') -Force -Recurse" & vbLf &
                "New-Item -ItemType Junction -Path (Join-Path '" & stateDir & "' 'logs') -Target '" & scratch & "' | Out-Null" & vbLf &
                "Write-Output ('JUNCTION|' + [bool](Get-RunLogDirectory))")
            Dim lines As String() = out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Assert.True(lines.Contains("NOLOGS|False"), "driver output: " & out)
            Assert.True(lines.Contains("LOGS|" & Path.Combine(stateDir, "logs")), "driver output: " & out)
            Assert.True(lines.Contains("JUNCTION|False"), "driver output: " & out)
            ' 'first' (no logs\ yet) went to %TEMP%; 'second' (logs\ present) into logs\, never into the state directory.
            Assert.Contains("first", File.ReadAllText(Path.Combine(temp, "enable-exchange-server.log")))
            Assert.DoesNotContain("second", File.ReadAllText(Path.Combine(temp, "enable-exchange-server.log")))
            Assert.False(File.Exists(Path.Combine(stateDir, "enable-exchange-server.log")))
        Finally
            Try
                ' The junction first, as a link: never delete through it.
                Dim junction As String = Path.Combine(scratch, "pd", "FastMediaSorterExchange", "logs")
                If Directory.Exists(junction) Then Directory.Delete(junction, False)
            Catch
            End Try
            ExchangeTestSupport.DeleteQuietly(scratch)
        End Try
    End Sub
    ''' <summary>
    ''' The helper's OWN functions on real folders (TestData\exchange-helper\logs-directory-driver.ps1 loads every
    ''' function of the script and none of its top-level code), without elevation: the current user stands in for
    ''' Administrators - the one owner a non-elevated process may set - and Authenticated Users for the enrolled
    ''' user. It proves the code paths of the elevated run: logs\ created with its final descriptor, adopted and
    ''' re-secured when something else made it, a wrong file descriptor caught and put right, a junction refused,
    ''' and the helper's own log opened inside logs\ only afterwards. What stays for the elevated hand run C-2 is
    ''' the real Administrators group, LocalService writing, and the SCM.
    ''' </summary>
    <Fact>
    Public Sub ServiceHelper_LogsDirectory_OnRealFolders_IsCreatedAdoptedAndVerified_WithBothFactsHolding()
        Dim driver As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "tests", "Companion.Tests", "TestData", "exchange-helper", "logs-directory-driver.ps1"))
        Dim out As String = RunPowerShell(driver, ServiceHelperPath())
        Dim facts As New Dictionary(Of String, String)()
        For Each line As String In out.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim bar As Integer = line.IndexOf("|"c)
            ' The helper's own timestamped log lines go to the host too; the facts are KEY|value.
            If bar > 0 AndAlso Not Char.IsDigit(line(0)) Then facts(line.Substring(0, bar)) = line.Substring(bar + 1)
        Next
        Assert.False(facts.ContainsKey("EXCEPTION"), If(facts.ContainsKey("EXCEPTION"), facts("EXCEPTION"), ""))

        ' 1. the first install
        Assert.Equal("True", facts("LOGSDIR-EXISTS"))
        Assert.EndsWith("\logs\install-exchange-service.log", facts("LOGPATH"))
        Assert.Equal("True", facts("HELPERLOG-HAS-LINE"))
        Assert.Equal("True", facts("HELPERLOG-NOT-IN-DATADIR"))
        '    fact 1: nothing for the enrolled user on the state directory; fact 2: read only on logs\
        Assert.Equal("", facts("DATA-PROBLEM"))
        Assert.Equal("", facts("LOGS-PROBLEM"))
        Assert.Equal("0", facts("DATA-ENROLLED-ENTRIES"))
        Assert.Equal("1", facts("LOGS-ENROLLED-ENTRIES"))
        Assert.Equal("ReadAndExecute, Synchronize", facts("LOGS-ENROLLED-RIGHTS"))
        Assert.Equal("True", facts("LOGS-PROTECTED"))
        '    LocalService: create entries here, Modify only on what is inside (no DELETE on the directory itself)
        Assert.Equal("Modify, Synchronize/InheritOnly ; Write, ReadAndExecute, Synchronize/None", facts("LOGS-LOCALSERVICE"))

        ' 2. a file the service made inherits exactly the directory's read-only entry for the user
        Assert.Equal("", facts("FILE-PROBLEM-INITIAL"))
        Assert.Equal("ReadAndExecute, Synchronize", facts("FILE-ENROLLED-RIGHTS"))
        Assert.Equal("True", facts("FILE-ENROLLED-INHERITED"))

        ' 3. a wrong descriptor on a file is caught, and the next pass puts it right
        Assert.Contains("holds more than read", facts("FILE-PROBLEM-BROKEN"))
        Assert.Equal("", facts("FILE-PROBLEM-FIXED"))

        ' 4. a logs\ somebody else made first is adopted and re-secured, and the helper's log reopens inside it
        Assert.Contains("inherits entries", facts("ADOPT-PROBLEM-BEFORE"))
        Assert.Equal("", facts("ADOPT-PROBLEM-AFTER"))
        Assert.Equal("", facts("ADOPT-FILE-PROBLEM"))
        Assert.Equal("True", facts("ADOPT-LOG-REOPENED"))

        ' 5. a junction named logs is refused (exit 11) and never followed
        Assert.Equal("True", facts("JUNCTION-REFUSED"))
        Assert.Equal("True", facts("JUNCTION-TARGET-UNTOUCHED"))
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

    Private Shared Function FirewallHelperPath() As String
        Return Path.Combine(InstallerDir(), FirewallHelperFile)
    End Function

    ''' <summary>Defines the verifier and descriptor functions of the service helper (extracted by AST; nothing else
    ''' of the script runs) with the script-level SIDs they read, then runs <paramref name="cases"/>.</summary>
    Private Shared Function RunAclDriver(helperPath As String, cases As String) As String
        Dim prelude As String =
            "param([string]$Helper)" & vbLf &
            "$ErrorActionPreference='Stop'" & vbLf &
            "$SidSystem='S-1-5-18'; $SidLocalService='S-1-5-19'; $SidAdmins='S-1-5-32-544'" & vbLf &
            "$ManageSid='S-1-5-21-1004336348-1177238915-682003330-1001'" & vbLf &
            "$ast=[System.Management.Automation.Language.Parser]::ParseFile($Helper,[ref]$null,[ref]$null)" & vbLf &
            "foreach ($n in 'Test-ManageSid','Split-Sids','New-AllowRule','New-LockedDirectorySecurity','New-DataDirSecurity','New-LogsDirSecurity','Get-SidDisplay','Get-LockdownAclProblem','Test-RightsWithin','Get-DataDirAclProblem','Get-LogsDirAclProblem','Get-LogFileAclProblem') {" & vbLf &
            "  $fd=$ast.Find({param($x) $x -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $x.Name -eq $n},$true)" & vbLf &
            "  if (-not $fd) { throw ('function ' + $n + ' is missing from the helper') }" & vbLf &
            "  Invoke-Expression $fd.Extent.Text" & vbLf &
            "}" & vbLf
        Return RunPowerShell(prelude & cases, helperPath)
    End Function

    ''' <summary>Defines Get-RunLogDirectory and Write-RunLog of the firewall helper with ProgramData and TEMP pointed at
    ''' scratch folders (the child process only), then runs <paramref name="cases"/>.</summary>
    Private Shared Function RunLogDriver(helperPath As String, programData As String, temp As String, cases As String) As String
        Dim prelude As String =
            "param([string]$Helper)" & vbLf &
            "$ErrorActionPreference='Stop'" & vbLf &
            "$env:ProgramData='" & programData & "'; $env:TMP='" & temp & "'; $env:TEMP='" & temp & "'" & vbLf &
            "$logName='enable-exchange-server.log'" & vbLf &
            "$ast=[System.Management.Automation.Language.Parser]::ParseFile($Helper,[ref]$null,[ref]$null)" & vbLf &
            "foreach ($n in 'Get-RunLogDirectory','Write-RunLog') {" & vbLf &
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
            ' The test host may itself run under PowerShell 7, whose module path Windows PowerShell 5.1 cannot load
            ' (Get-Acl then fails to auto-load): the child starts from its own defaults, as the installer's does.
            psi.Environment.Remove("PSModulePath")
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

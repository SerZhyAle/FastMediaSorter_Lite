<#
.SYNOPSIS
    The DynAudit fixture window: a small WinForms window the harness owns, driven exactly
    the way the product is driven - from another process, by window handle. The self-test
    (specification 209 section 4) verifies every probe against this fixture before any
    scene counts; the control run (section 7) runs the probes against it too.

.DESCRIPTION
    Run as a child process. The window answers a fixed private message set:
      WM_APP+1  allocate wParam GDI objects (each a distinct Pen - one handle each)
      WM_APP+2  free every allocated GDI object
      WM_APP+3  release the held file lock (opened at start from -LockFile)
      WM_APP+4  close the window and exit
    F5 advances the caption counter ("DynAuditFixture:<n>") - the injected-key probe
    polls that caption as its visible effect. The controls carry names for the UIA walk.
    The HWND and PID are written to -ReadyFile once the window is shown.
#>
param(
    [Parameter(Mandatory = $true)][string]$ReadyFile,
    [string]$LockFile = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# The compile needs the desktop assemblies referenced explicitly: Add-Type's default
# reference set does not include WinForms under pwsh. The type-forward targets WinForms
# splits into (Primitives, ComponentModel) are preloaded by simple name (they sit in the
# process's trusted platform assemblies), then every loaded assembly's path is a valid
# Roslyn reference - which closes the transitive closure without a hand-kept list.
foreach ($preload in @('System.Windows.Forms.Primitives', 'System.ComponentModel.Primitives')) {
    try { [void][System.Reflection.Assembly]::Load($preload) } catch { }
}
$fixtureRefs = @([AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.Location } |
    ForEach-Object { $_.FullName })
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DynAudit
{
    public class FixtureForm : Form
    {
        public const int WM_ALLOC = 0x8001;   // wParam = count
        public const int WM_FREE = 0x8002;
        public const int WM_RELEASE_LOCK = 0x8003;
        public const int WM_EXIT = 0x8004;

        // Real GDI objects, so GetGuiResources(GR_GDIOBJECTS) sees exactly N of them -
        // GDI+ wrappers (System.Drawing.Pen and friends) do not appear in that counter.
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        private readonly List<IntPtr> _gdi = new List<IntPtr>();
        private FileStream _lock;
        private int _keys;

        public FixtureForm(string lockFile)
        {
            Text = "DynAuditFixture:0";
            Width = 320; Height = 160;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var label = new Label(); label.Text = "Fixture label"; label.Top = 10; label.Left = 10; label.Width = 200;
            var check = new CheckBox(); check.Text = "Fixture toggle"; check.Top = 40; check.Left = 10; check.Width = 200;
            var button = new Button(); button.Text = "Fixture button"; button.Top = 70; button.Left = 10; button.Width = 120;
            Controls.Add(label); Controls.Add(check); Controls.Add(button);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { _keys++; Text = "DynAuditFixture:" + _keys.ToString(); e.Handled = true; }
            };

            if (!string.IsNullOrEmpty(lockFile))
                _lock = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ALLOC)
            {
                int n = unchecked((int)(long)m.WParam);
                if (n < 0 || n > 100000) n = 0;
                for (int i = 0; i < n; i++)
                {
                    uint color = (uint)((i & 0xFF) | (((i >> 8) & 0xFF) << 8) | (0x40 << 16));
                    IntPtr h = CreatePen(0, 1, color);   // PS_SOLID
                    if (h != IntPtr.Zero) _gdi.Add(h);
                }
                m.Result = new IntPtr(_gdi.Count);
                return;
            }
            if (m.Msg == WM_FREE)
            {
                foreach (IntPtr h in _gdi) { try { DeleteObject(h); } catch { } }
                int freed = _gdi.Count;
                _gdi.Clear();
                m.Result = new IntPtr(freed);
                return;
            }
            if (m.Msg == WM_RELEASE_LOCK)
            {
                if (_lock != null) { try { _lock.Dispose(); } catch { } _lock = null; m.Result = new IntPtr(1); }
                else m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_EXIT)
            {
                Close();
                return;
            }
            base.WndProc(ref m);
        }
    }
}
'@ -ReferencedAssemblies $fixtureRefs -ErrorAction Stop

$form = [DynAudit.FixtureForm]::new($LockFile)
$form.Add_Shown({
    # Start-Process -WindowStyle of the parent would otherwise minimize the first window
    # the child shows; the fixture must be visible for the pix probe and focusable for
    # the key probe, so it restores itself before it announces readiness.
    try { $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal } catch { }
    try { Set-Content -LiteralPath $ReadyFile -Value ("{0}`t{1}" -f $PID, $form.Handle) -Encoding ASCII } catch { }
})
try { [void][System.Windows.Forms.Application]::Run($form) } finally {
    if (Test-Path -LiteralPath $ReadyFile) { Remove-Item -LiteralPath $ReadyFile -Force -ErrorAction SilentlyContinue }
}

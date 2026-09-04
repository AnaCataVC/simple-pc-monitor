using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace SimplePCMonitor.Installer
{
    public partial class SetupWindow : Window
    {
        private int _currentStep = 0;
        private string _targetDirectory;

        public SetupWindow()
        {
            InitializeComponent();

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _targetDirectory = Path.Combine(localApp, "Programs", "SimplePCMonitor");
            TxtInstallDir.Text = _targetDirectory;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 1)
            {
                ShowStep(0);
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 0)
            {
                ShowStep(1);
            }
            else if (_currentStep == 1)
            {
                StartInstallation();
            }
            else if (_currentStep == 3)
            {
                if (ChkLaunchAfter.IsChecked == true)
                {
                    string targetExe = Path.Combine(_targetDirectory, "SimplePCMonitor.exe");
                    if (File.Exists(targetExe))
                    {
                        Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                    }
                }
                Close();
            }
        }

        private void ShowStep(int step)
        {
            _currentStep = step;
            PageWelcome.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageOptions.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageInstalling.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            PageFinished.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            BtnBack.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            BtnCancel.Visibility = (step == 0 || step == 1) ? Visibility.Visible : Visibility.Collapsed;

            if (step == 0)
            {
                BtnNext.Content = "Next >";
            }
            else if (step == 1)
            {
                BtnNext.Content = "Install";
            }
            else if (step == 2)
            {
                BtnNext.Visibility = Visibility.Collapsed;
                BtnBack.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
            }
            else if (step == 3)
            {
                BtnNext.Visibility = Visibility.Visible;
                BtnNext.Content = "Finish";
                BtnBack.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
            }
        }

        private void StartInstallation()
        {
            ShowStep(2);

            bool createDesktop = ChkDesktopShortcut.IsChecked == true;
            bool createStartMenu = ChkStartMenu.IsChecked == true;
            bool launchOnStartup = ChkStartup.IsChecked == true;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    // 1. Create target directory
                    UpdateStatus("Creating directory...", 20);
                    if (!Directory.Exists(_targetDirectory))
                    {
                        Directory.CreateDirectory(_targetDirectory);
                    }

                    // 2. Extract payload files
                    UpdateStatus("Installing program files...", 50);
                    string targetExe = Path.Combine(_targetDirectory, "SimplePCMonitor.exe");
                    string targetIco = Path.Combine(_targetDirectory, "icon.ico");
                    string targetPng = Path.Combine(_targetDirectory, "icon.png");

                    ExtractResourceOrCopy("SimplePCMonitor.exe", targetExe);
                    ExtractResourceOrCopy("icon.ico", targetIco);
                    ExtractResourceOrCopy("icon.png", targetPng);

                    // 3. Create shortcuts
                    UpdateStatus("Creating system shortcuts...", 75);
                    if (createDesktop)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        CreateShortcut(Path.Combine(desktopPath, "Simple PC Monitor.lnk"), targetExe, targetIco);
                    }

                    if (createStartMenu)
                    {
                        string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                        string programsDir = Path.Combine(startMenu, "Programs");
                        CreateShortcut(Path.Combine(programsDir, "Simple PC Monitor.lnk"), targetExe, targetIco);
                    }

                    if (launchOnStartup)
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null)
                            {
                                key.SetValue("SimplePCMonitor", string.Format("\"{0}\"", targetExe));
                            }
                        }
                    }

                    // 4. Create Uninstaller script & Registry Entry
                    UpdateStatus("Registering in Windows Application Manager...", 90);
                    RegisterUninstaller(targetExe, targetIco);

                    System.Threading.Thread.Sleep(500);
                    UpdateStatus("Finalizing...", 100);
                    System.Threading.Thread.Sleep(300);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShowStep(3);
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show("Installation error: " + ex.Message, "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Close();
                    }));
                }
            });
        }

        private void UpdateStatus(string message, int progress)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtInstallStatus.Text = message;
                ProgInstall.Value = progress;
            }));
        }

        private void ExtractResourceOrCopy(string filename, string targetPath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resourceName = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(filename, StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName != null)
                {
                    using (var stream = asm.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                            {
                                stream.CopyTo(fs);
                            }
                            return;
                        }
                    }
                }

                // Fallback: Copy from local directory if resource was not found
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string localFile = Path.Combine(baseDir, filename);
                if (File.Exists(localFile))
                {
                    File.Copy(localFile, targetPath, true);
                }
            }
            catch { }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string iconPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetExe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
                    shortcut.Description = "Simple PC Monitor - Real-Time Performance Dashboard";
                    if (File.Exists(iconPath))
                    {
                        shortcut.IconLocation = iconPath;
                    }
                    shortcut.Save();
                }
            }
            catch { }
        }

        private void RegisterUninstaller(string exePath, string iconPath)
        {
            try
            {
                string uninstallScript = Path.Combine(_targetDirectory, "Uninstall.cmd");
                string scriptContent = string.Format(
                    "@echo off\r\n" +
                    "taskkill /f /im SimplePCMonitor.exe >nul 2>&1\r\n" +
                    "del /q \"{0}\\Simple PC Monitor.lnk\" >nul 2>&1\r\n" +
                    "del /q \"{1}\\Programs\\Simple PC Monitor.lnk\" >nul 2>&1\r\n" +
                    "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SimplePCMonitor\" /f >nul 2>&1\r\n" +
                    "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"SimplePCMonitor\" /f >nul 2>&1\r\n" +
                    "echo Simple PC Monitor has been uninstalled successfully.\r\n" +
                    "timeout /t 2 >nul\r\n" +
                    "rmdir /s /q \"{2}\" >nul 2>&1\r\n",
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    _targetDirectory
                );
                File.WriteAllText(uninstallScript, scriptContent);

                string uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SimplePCMonitor";
                using (var key = Registry.CurrentUser.CreateSubKey(uninstallKey))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Simple PC Monitor");
                        key.SetValue("DisplayVersion", "2.3.0");
                        key.SetValue("Publisher", "AnaCata");
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("UninstallString", string.Format("cmd.exe /c \"{0}\"", uninstallScript));
                        key.SetValue("InstallLocation", _targetDirectory);
                        key.SetValue("EstimatedSize", 1024);
                    }
                }
            }
            catch { }
        }
    }
}

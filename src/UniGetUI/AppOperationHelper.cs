using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Controls;
using UniGetUI.Controls.OperationWidgets;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.Interface;
using UniGetUI.Interface.Telemetry;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Managers.PowerShellManager;
using UniGetUI.PackageEngine.Operations;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageOperations;
using UniGetUI.Pages.DialogPages;

namespace UniGetUI;

public partial class MainApp
{
    public static class Operations
    {
        public static bool AreThereRunningOperations()
        {
            return _operationList.Any() &&
                   _operationList.Any(x => x.Operation.Status is OperationStatus.Running or OperationStatus.InQueue);
        }

        public static ObservableCollection<OperationControl> _operationList = new();

        public static void Add(AbstractOperation op)
            => _operationList.Add(new(op));

        public static void Remove(OperationControl control)
            => _operationList.Remove(control);

        public static void Remove(AbstractOperation op)
        {
            foreach(var control in _operationList.Where(x => x.Operation == op).ToArray())
            {
                _operationList.Remove(control);
            }
        }

        /*
         *
         * OPERATION CREATION HELPERS
         *
         */
        public static async Task<AbstractOperation?> AskLocationAndDownload(IPackage? package, TEL_InstallReferral referral)
        {
            if (package is null) return null;
            try
            {
                DialogHelper.ShowLoadingDialog(CoreTools.Translate("Please wait..."));

                var details = package.Details;
                await details.Load();

                if (details.InstallerUrl is null)
                {
                    DialogHelper.HideLoadingDialog();
                    var dialog = new ContentDialog { Title = CoreTools.Translate("Download failed"),
                        Content = CoreTools.Translate("No applicable installer was found for the package {0}", package.Name),
                        PrimaryButtonText = CoreTools.Translate("Ok"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = MainApp.Instance.MainWindow.Content.XamlRoot,
                    };
                    await MainApp.Instance.MainWindow.ShowDialogAsync(dialog);
                    return null;
                }

                FileSavePicker savePicker = new();
                MainWindow window = MainApp.Instance.MainWindow;
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
                savePicker.SuggestedStartLocation = PickerLocationId.Downloads;

                string extension = details.InstallerUrl.ToString().Split('.')[^1];
                savePicker.SuggestedFileName = package.Id + " installer." + extension;

                if (package.Manager is BaseNuGet)
                {
                    extension = "nupkg";
                    savePicker.FileTypeChoices.Add("Compressed file", [".zip"]);
                }

                savePicker.FileTypeChoices.Add("Default", [$".{extension}"]);

                StorageFile file = await savePicker.PickSaveFileAsync();

                DialogHelper.HideLoadingDialog();
                if (file is not null)
                {
                    var op = new DownloadOperation(package, file.Path);
                    op.OperationSucceeded += (_, _) => TelemetryHandler.DownloadPackage(package, TEL_OP_RESULT.SUCCESS, referral);
                    op.OperationFailed += (_, _) => TelemetryHandler.DownloadPackage(package, TEL_OP_RESULT.FAILED, referral);
                    Add(op);
                    return op;
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while downloading the installer for the package {package.Id}");
                Logger.Error(ex);
                return null;
            }
        }

        /*
         * PACKAGE INSTALLATION
         */
        public static async Task<AbstractOperation?> Install(IPackage? package, TEL_InstallReferral referral,
            bool? elevated = null, bool? interactive = null, bool? no_integrity = null, bool ignoreParallel = false,
            AbstractOperation? req = null)
        {
            if (package is null) return null;

            var options = await InstallationOptions.FromPackageAsync(package, elevated, interactive, no_integrity);
            var op = new InstallPackageOperation(package, options, ignoreParallel, req);
            Add(op);
            op.OperationSucceeded += (_, _) => TelemetryHandler.InstallPackage(package, TEL_OP_RESULT.SUCCESS, referral);
            op.OperationFailed += (_, _) => TelemetryHandler.InstallPackage(package, TEL_OP_RESULT.FAILED, referral);
            return op;
        }

        public static void Install(IReadOnlyList<IPackage> packages, TEL_InstallReferral referral, bool? elevated = null, bool? interactive = null, bool? no_integrity = null)
        {
            foreach (var package in packages)
            {
                _ = Install(package, referral, elevated, interactive, no_integrity);
            }
        }

        /*
         * PACKAGE UPDATE
         */
        public static async Task<AbstractOperation?> Update(IPackage? package, bool? elevated = null, bool? interactive = null, bool? no_integrity = null, bool ignoreParallel = false, AbstractOperation? req = null)
        {
            if (package is null) return null;

            var options = await InstallationOptions.FromPackageAsync(package, elevated, interactive, no_integrity);
            var op = new UpdatePackageOperation(package, options, ignoreParallel, req);
            Add(op);
            op.OperationSucceeded += (_, _) => TelemetryHandler.UpdatePackage(package, TEL_OP_RESULT.SUCCESS);
            op.OperationFailed += (_, _) => TelemetryHandler.UpdatePackage(package, TEL_OP_RESULT.FAILED);
            return op;
        }

        public static void Update(IReadOnlyList<IPackage> packages, bool? elevated = null, bool? interactive = null, bool? no_integrity = null)
        {
            foreach (var package in packages)
            {
                _ = Update(package, elevated, interactive, no_integrity);
            }
        }

        /*
         * PACKAGE UNINSTALL
         */

        public static async void ConfirmAndUninstall(IReadOnlyList<IPackage> packages, bool? elevated = null, bool? interactive = null, bool? remove_data = null)
        {
            if (!await DialogHelper.ConfirmUninstallation(packages))
                return;

            Uninstall(packages, elevated, interactive, remove_data);
        }

        public static async void ConfirmAndUninstall(IPackage? package, bool? elevated = null, bool? interactive = null, bool? remove_data = null)
        {
            if (package is null) return;
            if (!await DialogHelper.ConfirmUninstallation(package)) return;

            _ = Uninstall(package, elevated, interactive, remove_data);
        }

        public static async Task<AbstractOperation?> Uninstall(IPackage? package, bool? elevated = null, bool? interactive = null, bool? remove_data = null, bool ignoreParallel = false, AbstractOperation? req = null)
        {
            if (package is null) return null;

            var options = await InstallationOptions.FromPackageAsync(package, elevated, interactive, remove_data: remove_data);
            var op = new UninstallPackageOperation(package, options, ignoreParallel, req);
            Add(op);
            op.OperationSucceeded += (_, _) => TelemetryHandler.UninstallPackage(package, TEL_OP_RESULT.SUCCESS);
            op.OperationFailed += (_, _) => TelemetryHandler.UninstallPackage(package, TEL_OP_RESULT.FAILED);
            return op;
        }

        public static void Uninstall(IReadOnlyList<IPackage> packages, bool? elevated = null, bool? interactive = null, bool? remove_data = null)
        {
            foreach (var package in packages)
            {
                _ = Uninstall(package, elevated, interactive, remove_data);
            }
        }
    }
}

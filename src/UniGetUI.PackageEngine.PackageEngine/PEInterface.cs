using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Managers.WingetManager;
using UniGetUI.PackageEngine.PackageLoader;

namespace UniGetUI.PackageEngine
{
    /// <summary>
    /// The interface/entry point for the UniGetUI Package Engine
    /// </summary>
    public static class PEInterface
    {
        private const int ManagerLoadTimeout = 60; // 60 seconds timeout for Package Manager initialization (in seconds)

        public static readonly WinGet WinGet = new();

        public static readonly IPackageManager[] Managers = [WinGet];

        public static readonly DiscoverablePackagesLoader DiscoveredPackagesLoader = new(Managers);
        public static readonly UpgradablePackagesLoader UpgradablePackagesLoader = new(Managers);
        public static readonly InstalledPackagesLoader InstalledPackagesLoader = new(Managers);
        public static readonly PackageBundlesLoader PackageBundlesLoader = new(Managers);

        public static void Initialize()
        {
            List<Task> initializeTasks = [];

            foreach (IPackageManager manager in Managers)
            {
                initializeTasks.Add(Task.Run(manager.Initialize));
            }

            Task ManagersMetaTask = Task.WhenAll(initializeTasks);
            try
            {
                ManagersMetaTask.Wait(TimeSpan.FromSeconds(ManagerLoadTimeout));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            if (ManagersMetaTask.IsCompletedSuccessfully == false)
            {
                Logger.Warn("Timeout: Not all package managers have finished initializing.");
            }

            _ = InstalledPackagesLoader.ReloadPackages();
            _ = UpgradablePackagesLoader.ReloadPackages();
        }
    }
}

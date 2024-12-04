﻿using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances
{
    class ServerLauncherViewModelInstances
    {
        public static ServerLauncherViewModel DEFAULT => new ServerLauncherViewModel(
            LauncherViewModelInstances.DEFAULT,
            SettingsViewModelInstances.DEFAULT,
            ServersViewModelInstances.DEFAULT,
            new ModManager(new HashMap<IModRegistry, IEnumerable<Mod>>(), new List<Release>()),
            "Chivalry 2 server",
            "Design time test description",
            "",
            "FFA_Courtyard",
            7777,
            9001,
            7071,
            3075,
            true,
            new FileBackedSettings<ServerSettings>("")
        );
    }
}

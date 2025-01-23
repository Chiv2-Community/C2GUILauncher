﻿using LanguageExt;
using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    using static LanguageExt.Prelude;

    public static class ModViewModelInstances {
        public static ModVM DEFAULT => CreateDefaultModViewModel();

        public static readonly ModManifest DesignViewManifest = new ModManifest(
            "https://github.com/Gooner/FinallyMod",
            "FinallyMod",
            "It has finally been done",
            "https://example.com",
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4",
            ModType.Shared,
            new List<string> { "Finally", "Gooner" },
            new List<Dependency> {
                        new Dependency("https://Gooner/BaseMod", "v1.0.0")
            },
            new List<ModTag> { ModTag.Cosmetic },
            new List<string> { "TDM_Dungeon" },
            new OptionFlags(false)
        );

        public static readonly Release DesignViewRelease = new Release("v1.0.0", "abcd", "ExamplePak", DateTime.Now, DesignViewManifest);

        public static readonly Core.JsonModels.Metadata.V3.Mod DesignViewMod = new Core.JsonModels.Metadata.V3.Mod(
            DesignViewManifest,
            new List<Release> {
                DesignViewRelease
            }
        );

        private static ModVM CreateDefaultModViewModel() {
            return new ModVM(
                DesignViewMod,
                Some(DesignViewRelease),
                // TODO: Set up some kind of in-memory registry for design view purposes
                new ModManager(new LocalModRegistry("foo", new LocalFilePakDownloader("bar")), new List<ReleaseCoordinates>())
            );
        }


    }
}
# Gillionaire

Gil trading plugin originally by [voidstar0](https://github.com/voidstar0/Gillionaire). This is the fork maintained by **kuchris**, with Dalamud API 15 support, a trade window, and fixes for consecutive trades.

## Install

Add this URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, enable it, and save:

```text
https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json
```

Open `/xlplugins`, find **Gillionaire**, and install it. The same repository also includes XIV AI Chat and MoreMacros. Installation packages for this fork are published under [kuchris/Gillionaire releases](https://github.com/kuchris/Gillionaire/releases).

Original author attribution is retained as `voidstar0`. This fork is maintained separately from the upstream project.

## How To Use

Target a player, then use `/giltrade` to open the Gillionaire window. Enter the total amount and select **Start trading**. Amounts over 1,000,000 gil are split across consecutive trades. You can also start immediately from chat with `/giltrade <amount>`.

https://github.com/user-attachments/assets/b64ee237-8180-40b0-933a-e2002f051444

### Activating as a development plugin

1. Launch the game and use `/xlsettings` in chat or `xlsettings` in the Dalamud Console to open up the Dalamud settings.
    * In here, go to `Experimental`, and add the full path to the `Gillionaire.dll` to the list of Dev Plugin Locations.
2. Next, use `/xlplugins` (chat) or `xlplugins` (console) to open up the Plugin Installer.
    * In here, go to `Dev Tools > Installed Dev Plugins`, and the `Gillionaire` should be visible. Enable it.

## Build

Requires Windows, .NET 10, and matching Dalamud API 15 development assemblies. Initialize the ECommons submodule, restore locked dependencies, and build:

```powershell
git submodule update --init --recursive
./build.ps1
```

The script restores locked dependencies, builds the Release configuration, and writes `artifacts/Gillionaire-<version>.zip`. The package includes the ECommons dependency and its license notice, with no debug symbols. After publishing this ZIP, update the `Gillionaire` entry in the shared [DalamudPlugins catalogue](https://github.com/kuchris/DalamudPlugins).

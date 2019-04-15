# INIfier.Bepin
Port of the INIfier plugin to BepInEx. You can find the original mod by Moojuiceman [here](https://www.nexusmods.com/site/mods/30). Below is a modified copy of the readme available on the original mod page in case it gets lost.

## Description
INIfier allows you to replace the content of a Unity TextAsset without updating/replacing the asset files themselves. This is mostly useful for developers that would need to replace TextAssets as part of their mods, and is not geared towards the end user. Despite this it could still be useful to users, for example if there is display text stored within a TextAsset that they would like to translate to another language.

Normally this would require updating the .assets files from the game and distributing the updated files or a patch, which may need to re-developed and distributed with each new game update. Using INIfier, as long as the name of the asset doesn't change, you can continue using the same replacement file between any game version. And as long as the file isn't overwritten or removed, it doesn't need to be re-created on every launch or game update.

## Installation (for end users)
1. Install [BepInEx](https://github.com/BepInEx/BepInEx
) for the target game.
2. Put the .dll inside the BepInEx directory.
3. Run the game, a new `Assets` folder should appear next to the .dll with assets dumped from the game.
4. Edit the assets that you want and change their extensions from .found to .ini to make INIfier read them. You might need to restart the game to see the changes.

The mod will be enabled by default, but both replacing and dumping can be toggled in config.ini as well as with the ConfigurationManager plugn. If replacing is enabled the mod will replace the text/bytes any time they are read from the TextAsset, if disabled then reading the text/bytes will return the original content. If the text from the TextAsset is stored in another variable or otherwise cached instead of being re-read from the asset each time, only the initial read will be handled by INIfier.

## Usage (for mod authors)
Reference the .dll in your project to use the INIfier methods.

There are 2 ways to replace a file
Provide the replacement .ini file with the mod so the user can place it in the INIfier/Assets directory
Register the replacement file programmatically

You can also use INIfier to check for the existence of a given found/replacement file, and get its contents. Used carefully, this allows multiple mods to update the same TextAsset by only modifying the affected part of the file, rather than each mod overwriting any previous changes.

The replacement string or byte array is stored in an .ini file, under the Assets folder inside the INIfier mod directory created by Unity Mod Manager. As long as you use the INIfier methods to manipulate replacement files, you don't have to care about the actual location of the Assets folder.

The file will be used instead of the "text" or "bytes" properties on the TextAsset instance. Each TextAsset has a name, and this must be the name of the replacement file. If no replacement file exists, INIfier will create a .found file (but will not overwrite an existing .found file) with the same name as the TextAsset, and place the contents inside it. Depending on whether the "text" or "bytes" property was called, the .found file will either be written as a string or a byte array. All files using the "text" property will be read/written as UTF8.

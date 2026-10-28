# Advanced Loader

A plugin that loads only specific parts of a card without overwriting the rest.

Supported games: Honey Select 2, Koikatsu Sunshine, Koikatsu.
Also built for AI-Shoujo, but not tested there yet.

| Honey Select 2 | Koikatsu Sunshine |
|---|---|
| ![Advanced load in Honey Select 2](AdvancedLoader/screenshots/5_hs2_ex.png) | ![Advanced load in Koikatsu Sunshine](AdvancedLoader/screenshots/7_kks_ex.png) |

## Features

- **Advanced load** button next to the maker's *Load character* and *Clothes > Load* pages.
- **Body categories** (Breast, Legs, Eyes, Nose, Makeup…), including the ABMX bonemod sliders.
- **Clothes categories** (Top, Bottom, Inner top/bottom, Gloves, Pantyhose, Socks, Shoes), including their Material Editor data and clothes overlays.
- **Accessories**: select which accessories to copy, then either overwrite the old ones or put them into free slots.
- **Material Editor groups** (Body, Head, Eyes, Hair, Clothes) with the *Clean copy* feature: changes of the group are reset before the card's changes are applied, so nothing from earlier edits is left behind.
- **Other plugins**: every plugin with data on the card is listed; only the ticked ones are loaded from the card, the others keep the character's current data.
- The game's original load windows are not changed.

### Koikatsu Sunshine / Koikatsu: outfits

- Load all or chosen outfits into the same, shifted or new outfit slots; the other outfits are kept.

![Outfits](AdvancedLoader/screenshots/6_kks_outfits.png)

### Other plugins

- Every plugin with data on the selected card is listed; tick the ones to load from the card.

![Other plugins](AdvancedLoader/screenshots/8_plugins.png)

## Screenshots

| Character | Accessories, Material Editor, other plugins |
|---|---|
| ![Character](AdvancedLoader/screenshots/1_body.png) | ![Accessories and Material Editor](AdvancedLoader/screenshots/2_body_acc_mat.png) |
| **Accessories into free slots** | **Clothes** |
| ![Accessories into free slots](AdvancedLoader/screenshots/3_acc.png) | ![Clothes](AdvancedLoader/screenshots/4_clothes.png) |

## Requirements

- [BepInEx](https://github.com/BepInEx/BepInEx) 5.4
- [HS2API / KKSAPI / AIAPI](https://github.com/IllusionMods/IllusionModdingAPI) 1.48 or newer, KKAPI 1.42.2 or newer

## Installation

Copy the file for your game into `BepInEx\plugins`:

- Honey Select 2: `HS2_AdvancedLoader.dll`
- Koikatsu Sunshine: `KKS_AdvancedLoader.dll`
- AI-Shoujo (not tested): `AI_AdvancedLoader.dll`
- Koikatsu: `KK_AdvancedLoader.dll`

## Usage

1. In the character maker open *Load character* or *Clothes > Load*.
2. Click the **Advanced load** button and select a card in the game's list.
3. Tick what to load and press **Load** in **Advanced load**.

A keyboard shortcut for the window can be set in the plugin settings (F1).

## Compatibility

| Plugin | HS2 / AI | KKS / KK |
|---|---|---|
| MoreAccessories | [1.2.2+](https://bitbucket.org/Joan6694/hsplugins) | [2.0.21+](https://github.com/jalil49/MoreAccessories) |
| [Material Editor](https://github.com/IllusionMods/KK_Plugins) | 4.0.3.1+ | 4.0.3+ |
| [ABMX](https://github.com/ManlyMarco/ABMX) | 5.2.2+ | 5.4+ |
| [Overlay Mods](https://github.com/ManlyMarco/Illusion-Overlay-Mods) | 6.3+ | 6.2+ |
| [More Outfit Slots](https://github.com/IllusionMods/KK_Plugins) | — | 1.1.2+ |
| [Maker/Studio Browser Folders](https://github.com/ManlyMarco/Illusion_BrowserFolders) | ✓ | ✓ |

## Changelog

- 1.2.0 — *Other plugins* lists every plugin with data on the card and keeps the unticked ones; unticked Material Editor groups stay as they were on Face/Body loads; Koikatsu version, untested AI-Shoujo version.
- 1.1.0 — Koikatsu Sunshine version, outfit selection for Koikatsu Sunshine.
- 1.0.0 — first public release.

## License

GPL-3.0, see [LICENSE](LICENSE).

# Advanced Loader

A Honey Select 2 plugin that loads only the parts you want from a character or clothes card in the character maker.

Version 1.0 supports Honey Select 2 only.

## Features

- **Advanced load** button next to the maker's *Load character* and *Clothes > Load* pages. It uses the card selected in the game's own list and shows character or clothes options depending on the page.
- **Face and Body by maker category** (Breast, Legs, Eyes, Nose, Makeup…), including the ABMX bonemod sliders shown in those categories.
- **Clothes by piece** (Top, Bottom, Inner top/bottom, Gloves, Pantyhose, Socks, Shoes), including their Material Editor data and clothes overlays.
- **Accessories by slot**: keep current, take all, or take only ticked slots, into the same slots, shifted by N slots, or into free slots.
- **Material Editor groups** (Body, Head, Eyes, Hair, Clothes) with *Clean copy*: changes of the group are reset before the card's changes are applied, so nothing from earlier edits is left behind. Groups that are not ticked keep the character's current materials.
- The game's original load windows are not changed.

## Requirements

- BepInEx 5.4
- HS2API 1.48.1 or newer

## Installation

Copy `HS2_AdvancedLoader.dll` into `BepInEx\plugins`.

## Usage

1. In the character maker open *Load character* or *Clothes > Load*.
2. Click the **Advanced load** button and select a card in the game's list.
3. Tick what to load and press **Load**.

A keyboard shortcut for the window can be set in the plugin settings (F1).

## Compatibility

- MoreAccessories 1.2.2+
- Material Editor 4.0.3.1+
- ABMX 5.2.2+
- Overlay Mods 6.3+
- Maker/Studio Browser Folders

## Changelog

- 1.0.0 — first public release (Honey Select 2 only).

## License

GPL-3.0, see [LICENSE](LICENSE).

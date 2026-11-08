# Bundled wallpapers

Copied next to the executable at build (`WharfBar.csproj`). Shown in the island hub under **WharfBar**. User images go in `%AppData%/WharfBar/wallpapers/` (**Yours**).

Gallery order follows file name (ordinal, case-insensitive).

| Prefix / set | Contents |
|--------------|----------|
| `alpine-lake-*`, `glass-rose-*`, `pier-*` | Abstract / scenic **light–dark pairs** (`name-light.ext` ↔ `name-dark.ext`, same folder) |
| `solid-*` | Flat colors (1920×1080): black, charcoal, dark-gray, white, soft-gray, navy, teal, warm-sand |
| `turkey-*` | City photos (toward end of list): Ankara Anıtkabir, Nevşehir fairy chimneys, İstanbul Yavuz Sultan Selim Bridge |
| `wharfbar-*` | Brand mark PNGs |

**Pairing and theme:** When a pair member was last applied from the island hub, WharfBar remembers the path in `island.lastHubWallpaper` and switches to the matching variant when the **resolved** app theme changes (Appearance light/dark, or Windows + WharfBar in **system** theme). Solids, `turkey-*`, and unpaired brand PNGs have no sibling — theme changes leave them alone. The gallery still lists light and dark as separate thumbnails; pairing is backend-only. See [`docs/05-theme-windows-wallpaper.md`](../../../docs/05-theme-windows-wallpaper.md).

AI-generated art and photos are project defaults; do not treat Downloads or external notes as the source of truth — keep new defaults here.

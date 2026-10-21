# Changelog

All notable changes to poe2pricer, newest first. The release notes on GitHub say the same
with more words: https://github.com/saxe78/poe2pricer/releases

## 1.2.0 — 2026-10-21

### Added
- Waystones tab: profiles (summary values, tier, revives, corrupted, mods in "and" / "or" /
  "not" groups), the search text for the stash search, and the profile's price on the trade
  site ("Refresh price").
- Tablets tab: the same for each kind of tablet, with the uses left.
- Live price at the tooltip of a waystone or tablet, from the trade site of pathofexile.com,
  asked in the words of the closest profile with the item's own values (without a profile,
  exactly as the item is): Settings → Display, "Live price from the trade site" and "incl.
  premium stash (online)". `Alt+D` ("Force live price") asks for the item exactly as it is,
  past the profiles.
- Waystones and tablets in the English and the German game client; the search text and the
  mod lists follow the client's language.
- Stash tabs inside a stash folder are found in the keyboard UI (contributed by
  gazevans-pixel, #2).
- The log's start line names the date.

### Changed
- Updates are downloaded and installed only after a click on the message; before, they were
  downloaded in the background and installed when you quit.
- A UI Text Size other than medium gives a hint only, without a tray notice (#1).
- When poe.ninja cannot be reached, the log names the reason (host, secure connection,
  time-out).
- Releases are built by GitHub Actions, with `SHA256SUMS.txt`.
- README and guide: waystones and tablets, with pictures of the program window.

### Fixed
- Favours: a tooltip over empty cells ended at the screen's edge at large text sizes and hid
  the items below it (#1).
- Island Rumours: the island was not recognised when its ship marker is drawn dimmer (#1).
- A diagnosis taken after the overlay hid itself reported the window shown before.

## 1.1.2 — 2026-10-20

### Fixed
- Item art is fetched only from web.poecdn.com: a poe.ninja answer naming another address is
  ignored instead of followed.
- The release page a zip copy opens on a click is taken from GitHub's answer only when it lies
  in this repository; anything else leads to the releases page.
- Answers from poe.ninja, the image server, the rumour table and GitHub are read up to 32 MB
  and dropped beyond.
- A diagnosis no longer holds the file paths from the settings (game settings file, data
  folder, the derived data path), which carry the Windows user name.

### Changed
- Item art is decoded by the Windows Imaging Component instead of the decoder in OpenCV, and
  only after a check of the file's header (PNG with alpha channel, at most 1024 pixels a
  side): the one place where data from the network met a parser shipped with the program.

### Added
- README: a "Trust" section (what the program reads, sends and runs; the keyboard hook; the
  unsigned updater; what a diagnosis contains).

## 1.1.1 — 2026-10-19

### Added
- The version number in the program window links to this changelog (opens in the browser).

### Fixed
- Two islands the community table has swapped ("Something Fishy" is Barren Atoll, "Bleak and
  Awful" is Bleached Shoals, as the game's own texts say) are shown as in the game, in the
  Rumours tab and under the line on the card.
- Island Rumours: a roll read right after a short absence of the panel counts; "complete" is
  lifted again when three rows return; the next island is no longer merged into the previous
  one in the controller UI when its first picture shows little of the chart; a reopening that
  shows less of the chart no longer replaces the richer reading the island is recognised by.
- The tier weights are saved even when the ratings file cannot be written.
- At 3840×2160 the quick check of an open window paced itself faster than its screen capture
  took; it and the automatic scan now keep to half the time at most.
- The program no longer writes pictures of its own into the diagnostics folder (1.0.0 and
  1.1.0 kept two full pictures of every reroll it detected in the Ritual window, without end).
  Only the diagnostics key writes there, unless "Extended diagnostics" (Settings → Hotkeys,
  off by default) is on; then at most three pictures of each kind stay.

## 1.1.0 — 2026-10-19

### Added
- Island Rumours are collected per island: the island is recognised by the sea chart around
  its ship marker; rumours no longer on the panel are listed under it, new ones are marked,
  and an expedition tier (the mean of the tiers seen, as a tier) sums the island up, with the
  number of rumours and rolls. Fewer than three on the panel means the island has no more;
  after enough rolls without anything new it says "probably complete". With three on the
  panel the card reminds you to move an item for more.
- Tier weights for the expedition tier in the Rumours tab.
- Prices in Exalted or Chaos: Settings → Display → "Prices in". From one Divine on prices
  stay in Divine either way; the price floor is compared in the unit shown.

### Known limits
- Dragging or zooming the Atlas between two openings of the panel starts a new collection
  for the same island; opening it without moving the Atlas is always recognised.

## 1.0.0 — 2026-10-18

First public release: prices from poe.ninja at the Ritual window (Favours), the Expedition
remnant list, the special stash tabs, and the community tiers of the Island Rumours beside
the panel. Setup with self-update, or zips.

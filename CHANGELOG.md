# Changelog

All notable changes to poe2pricer, newest first. The release notes on GitHub say the same
with more words: https://github.com/saxe78/poe2pricer/releases

## 1.1.0 — 2026-10-20

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

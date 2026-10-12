# LupiraGeoApi — repo rules

Docs: `docs/` (architecture, event-sourcing).

## Scope
- Geo is atemporal: `Place` is a shared catalog entry, `SavedPlace` a per-user bookmark. No ValidFrom/ValidTo.
- Periods (residence, employment) and trips belong in LupiraLocationApi, referencing place ids.
- Addresses (homes included) are catalog `Place`s; residencies reference them by id. Personal labels ("Home") stay `SavedPlace`. Commit a picked hit with `POST /places/from-geocode` (OSM id), a map pin with `POST /places/from-pin`, free text with `resolve_place`.
- AdminAreas (Country/Region/Locality) have no MCP surface; they are built lazily on geocode.

## Geocoding
- Nominatim is the canonical resolver; Photon (`Photon__BaseUrl`, empty = off) is address autocomplete only, behind `IAddressAutocomplete`. Vendor names stay in the adapter.
- `intent=Typeahead` never reaches the public Nominatim fallback and is never cached. `intent=Search` asks Nominatim (cached), then Photon when Nominatim has nothing.
- Query enums bind case-sensitively by name: `intent=Typeahead`, `precision=Building`.
- `from-geocode` without a frozen query hit: `/lookup` regional → public → the client's hit fields as an approximate place. Default names come from address components, never the typed query.
- `RegeocodeAsync` geocodes `FormattedAddress` verbatim (`CanonicalName` only when blank). A venue-prefixed address misses; heal with a street-only `FormattedAddress`, then `regeocode_place`, which also builds containment.
- A numbered address geocoded only to its street (or another number) is an approximate fix: coordinates and containment, no OSM id, no `FormattedAddress`. Dedupe never joins different house numbers (`HouseNumber`).
- `update_place` lat/lon alone does not build containment; regeocode or pass `withinAreaId`.
- The regional Nominatim answers bare or foreign names with local lookalikes and ignores postcodes. `ForwardHitFilter` (postcode region check, importance bar for bare words, `Nominatim__RegionalCountries`) rejects those so the next endpoint is asked.
- `PlaceKind.Area` is a whole settlement or admin area, classified from the geocode cache. `classify_area_places` is dry-run by default; re-run the dry run after bulk imports.

## Curation
- `merge_places` turns the loser into a tombstone redirect (`MergedIntoId`) so ids held by other services keep resolving; readers canonicalize through geo, nobody rewrites stored ids. Use merge for duplicates, `delete_place` for wrong entries with no survivor.
- Merge keeps the survivor's fields; coordinates/address/containment/OSM id are one fix, taken from the loser only when the survivor has no coordinates.
- `delete_place` is a soft delete (`DeletedAt`) and does not check references. Run `list_orphans` first; `prune_places` re-checks per id.
- Batch resolve and regeocode are capped at 50 per call; over-cap is rejected whole. A per-item geocoder outage returns `GeocodeUnavailable`/`Unavailable` and creates nothing; re-run those items.
- A self-alias equal to the canonical name is rejected.

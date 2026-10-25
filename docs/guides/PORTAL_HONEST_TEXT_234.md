# Exchange content privacy - portal hand-off

Updated: 2026-10-23. Implementation and local evidence: specification 234.

The portal is maintained outside this repository. No portal deployment or cross-device acceptance is
claimed by the Windows implementation.

Use the complete, verbatim text of `DEVICE-EXCHANGE` section 27 item DE in every portal surface that
describes exchange-server visibility. The EN, RU and UK strings are available as `honestEN`, `honestRU`
and `honestUK` in [the Windows server's statement source](../../exchange-server/internal/statement/honest.go).
[The privacy page](../privacy.html) contains these same strings. Do not substitute the earlier
seven-point statement about clear relay, tunnel or clipboard content for a server advertising `sealed`.
Describe device names, presence, sizes and timing as visible metadata, and retain the account-device
insertion caveat in the canonical paragraph.

Where a surface describes the Google Drive channel, include item DE's complete Drive text from the
catalog. Item DO keeps that channel's files and clipboard in the person's own Google account; it is
not an exchange-server transfer. Apply the channel's existing publication gate before adding that
description to a public surface.

The Windows app supplies all 13 UI translations; the catalog's EN, RU and UK remain the normative
wording. Language review of the other ten translations and the portal's own render and publication
checks belong to the receiving project. Names follow [the product naming guide](PRODUCT_NAMING.md).

The final joint folder, sealed broadcast and clipboard run remains specification 205 with Android
S4244. The owner excluded that run from the 2026-10-23 local completion because the test devices were
unavailable. Keep that evidence distinct from local automated tests.

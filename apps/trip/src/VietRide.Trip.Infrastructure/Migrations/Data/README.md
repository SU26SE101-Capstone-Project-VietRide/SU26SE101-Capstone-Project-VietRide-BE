# Vietnam administrative catalog 2025

This directory contains the normalized two-level administrative catalog used by Trip Service.

- Legal source: Decision 19/2025/QD-TTg dated 2025-06-30, effective 2025-07-01.
- Official document page: https://vanban.chinhphu.vn/?classid=1&docid=214409&orggroupid=3&pageid=27160
- Signed decision PDF: https://datafiles.chinhphu.vn/cpp/files/vbpq/2025/7/19ttg.signed.pdf
- Machine-readable appendix published on `datafiles.chinhphu.vn`:
  https://datafiles.chinhphu.vn/cpp/files/duthaovbpl/3.phu-luc-danh-muc-va-ma-so-don-vi-hanh-chinh-moi-cap-tinh-cap-xa-17.6.xlsx

The normalized CSV contains 34 province/municipality rows and 3,321 ward/commune/special-zone
rows. Codes are UTF-8 strings and preserve official leading zeroes. Names are trimmed and Unicode
NFC-normalized. Leaf types are derived only from the official `Phường`, `Xã`, and `Đặc khu`
prefixes. The parent code is resolved from the province/municipality column in the appendix.

Checksums (SHA-256):

- Source XLSX: `7a41ac3dc5e4a5e6402c959a79e964df7563cb33bfa0bcca94fa89b85f95860d`
- Signed PDF: `2fd391335947affbfb86fa2b9438f0fdca90694e80887e205266542c3236aea1`
- Normalized CSV: `ae62af9c377e668096cc303871d2456758ad807199b8586c182491b7f55f94e2`
- Generated Up SQL: `03aa55a029826d273a30ff8c565124d7ae3ca9cac4a210177cd44f2fc8240b63`
- Generated Down SQL: `4d22d76dfe81f5aa17d28def17a2d7892293f1a6d44d08fb17ddbec06ea99f29`

The migration preserves existing top-level Location IDs by updating the previous 34 seed codes to
their official two-digit codes. Leaf IDs are deterministic UUIDv5 values derived from the official
five-digit code. The Down SQL deletes only those deterministic catalog IDs before restoring the
legacy top-level codes.

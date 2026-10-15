# Large-cache patch evidence

## Qualified image

Xe2 driver 32.0.101.9034, original SHA-256 `1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229`. All addresses below apply only to this image.

The constructor at RVA 0x18C850 sets +0x08 to 4 GiB and +0x10 to 512 MiB. Copies preserve these as cache object fields +0x948 and +0x950. Append/update code at 0x17A370 compares the running total +0xBE0 against +0x948. Reload at 0x17B4E0 checks file size against +0x950 and allocates/zeroes the whole file length. The read helper at 0x17CCF0 uses one ReadFile call with a DWORD transfer count, so a single request cannot represent a file larger than 4 GiB.

## Implemented patch

The experimental engine raises both constructor fields and redirects the full-cache reload call at RVA 0x17B620 to an appended assembly loop. It calls the original reader in chunks no larger than 256 MiB and retains the original asynchronous/error handling. The appended section includes unwind data and an extended sorted exception table; the PE checksum is recalculated. Contiguous allocation and parser ownership remain unchanged.

| Limit | Output SHA-256 |
| --- | --- |
| 8 GiB | `BF74F45935E6BEB8B958728F96D49E7384C99ACEE0B82247981A1FC820C7405C` |
| 16 GiB | `D7B7D5092232165AA2B37A181FD0940C5167C2EEFD1D696FCAC83C1159CB3132` |

Normalization restores the original image hash. Exact bytes, payload, PE mapping and unwind metadata are validated. These definitions cannot be applied to Xe1, Xe3 or another driver version.

## Recorded tests

- Six lifted-reader boundary fixtures reproduce DWORD request truncation while preserving 64-bit file offsets. Four allocator fixtures verify whole-file allocation/zeroing requests without allocating large buffers.
- Eight algorithm fixtures cover simulated 4/8/16 GiB reads, partial transfers, EOF, errors, overflow and invalid counts. Thirteen bounded-reader checks include a real sparse 8 GiB + 4096-byte file with marker data across 4 GiB; the fixture uses fixed-size buffers and is not integrated into Intel's parser.
- Eight native wrapper checks include Windows exception unwinding. Nineteen patch checks include deterministic hashes, mutation rejection, byte-identical rollback and PE function-table lookup. Six temporary-cohort checks cover install, ACL equivalence, backups and removal.
- Isolated B580 probes loaded both experimental copies from private directories, checked disk hashes, and completed D3D12 device and trivial compute-pipeline creation. No GPU work was dispatched and no game was launched by those probes.

## Limits of the evidence

Real shader-cache growth, gameplay and cache reload beyond 4 GiB remain unverified. The patch does not provide bounded-memory parsing. A large cache can require approximately its file size in contiguous RAM in addition to game memory. A configured ceiling is not an immediate allocation. Device/pipeline startup is not evidence of effective cache capacity or improved performance. No Intel driver files are redistributed.

The Windows transfer-count contract is documented by [Microsoft ReadFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-readfile).

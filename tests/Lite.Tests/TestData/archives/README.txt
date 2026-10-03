Fixtures for ArchiveSequentialTests (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md, phase F2).

Real 7-Zip output, because nothing in the test process can write a 7z (SharpCompress reads it
only). The payloads are System.Random(seed).NextBytes(), so they do not compress and the
expected bytes are fixed; manifest.txt holds "archives|entry|size|sha256" for each.

  solid.7z, nonsolid.7z   ch1/p1.jpg (6000 B, seed 1), ch1/p2.jpg (9000, seed 2),
                          ch2/p3.png (4000, seed 3), notes.txt (196 B text)
                          7z a -t7z -ms=on|off -mx=1 <name> ch1 ch2 notes.txt
  solid-many.7z           many/p01.jpg .. p12.jpg, 24000 B each, seeds 101..112 (solid)
  encrypted-headers.7z    the solid.7z content, password "secret", headers encrypted (-mhe=on)
  encrypted-data.7z       the same, headers in the clear (-mhe=off)

To make them again, run the 7z commands above over files generated the same way; the
manifest then stays valid because the payloads are deterministic.

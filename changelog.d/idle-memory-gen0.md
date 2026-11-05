### Changed

- The image sets `DOTNET_GCgen0size=0x200000`, a smaller first-generation budget for the .NET
  garbage collector. Every configuration idles about 4 MB lower, so an instance with an operator
  passkey in use now idles around 145 MB instead of 149 MB on arm64. It is set in the image, so a
  container gets it without any setting of yours; running the app outside the image is not
  supported, and would not get it.

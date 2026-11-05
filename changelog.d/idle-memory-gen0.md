### Changed

- The image sets `DOTNET_GCgen0size=0x200000`, a smaller first-generation budget for the .NET
  garbage collector. Every configuration idles about 4 MB lower, so an instance with an operator
  passkey in use now idles around 145 MB instead of 149 MB on arm64. Leave it as it is: removing it
  from the container's environment brings back the old figures.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore from the project file alone, so a code edit keeps the NuGet layer.
COPY Umbral.Sample.AspNetCore.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV PORT=5000
EXPOSE 5000
USER $APP_UID
ENTRYPOINT ["dotnet", "Umbral.Sample.AspNetCore.dll"]

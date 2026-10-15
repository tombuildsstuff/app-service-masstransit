# Build stage: restore and publish the app with the .NET SDK. It runs on the
# build machine's own architecture and publishes for the target one, so a
# multi-arch build doesn't run the SDK under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Orders.csproj ./
RUN dotnet restore -a $TARGETARCH
COPY src ./src
RUN dotnet publish -c Release -a $TARGETARCH -o /app --no-restore

# Runtime stage: the ASP.NET Core runtime and the published app only.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV PORT=8080
COPY --from=build /app ./
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Orders.dll"]

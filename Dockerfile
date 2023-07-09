FROM mcr.microsoft.com/dotnet/runtime:7.0 AS runtime
WORKDIR /app
COPY ./src/out ./

ENV LOCALE=en-US \
    TZ=America/Chicago

VOLUME ["/data"]
ENTRYPOINT ["dotnet", "geosvc.dll"]
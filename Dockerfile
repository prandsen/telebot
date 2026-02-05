FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

# Install python and ffmpeg. Create a virtual environment and install yt-dlp into it
# to avoid errors when the distribution manages the system python installation (PEP 668).
RUN apt-get update && \
    apt-get install -y python3 python3-venv python3-pip ffmpeg && \
    python3 -m venv /opt/venv && \
    /opt/venv/bin/pip install --upgrade pip wheel && \
    /opt/venv/bin/pip install yt-dlp && \
    ln -s /opt/venv/bin/yt-dlp /usr/local/bin/yt-dlp && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Telebot/ ./Telebot/
WORKDIR /src/Telebot

RUN dotnet restore Telebot.csproj
RUN dotnet publish -c Release -o /out

FROM base AS final
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Telebot.dll"]

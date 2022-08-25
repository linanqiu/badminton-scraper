FROM mcr.microsoft.com/dotnet/sdk:3.1.422-focal

# selenium dependencies
RUN apt-get update \
    && apt-get install -y libglib2.0-dev libnss3 libx11-6

# microsoft edge
RUN curl https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.gpg \
    && install -o root -g root -m 644 microsoft.gpg /etc/apt/trusted.gpg.d/ \
    && sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/edge stable main" > /etc/apt/sources.list.d/microsoft-edge-dev.list' \
    && rm microsoft.gpg \
    && apt update && apt install -y microsoft-edge-stable

# project
WORKDIR /src

RUN apt-get install -y zip \
    && wget https://msedgedriver.azureedge.net/104.0.1293.63/edgedriver_linux64.zip -P ./ \
    && unzip ./edgedriver_linux64.zip -d ./ \
    && rm ./edgedriver_linux64.zip

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app

WORKDIR /app

CMD ["dotnet", "BadmintonScraperConsole.dll"]
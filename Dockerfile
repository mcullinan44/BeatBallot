FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["BeatBallot.Web/BeatBallot.Web.csproj", "BeatBallot.Web/"]
COPY ["BeatBallot.Data/BeatBallot.Data.csproj", "BeatBallot.Data/"]
COPY ["BeatBallot.DataModels/BeatBallot.Models.csproj", "BeatBallot.DataModels/"]
COPY ["BeatBallot.Entities/Entertainer.Entities.csproj", "BeatBallot.Entities/"]
COPY ["BeatBallot.Services/BeatBallot.Services.csproj", "BeatBallot.Services/"]
RUN dotnet restore "BeatBallot.Web/BeatBallot.Web.csproj"

COPY . .
WORKDIR "/src/BeatBallot.Web"
RUN dotnet publish "BeatBallot.Web.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BeatBallot.Web.dll"]

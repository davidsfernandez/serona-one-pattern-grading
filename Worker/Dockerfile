# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS publish
WORKDIR /src
COPY GestionProduccion.Shared/ GestionProduccion.Shared/
COPY GestionProduccion.Domain/ GestionProduccion.Domain/
COPY GestionProduccion.Nesting/Core/ GestionProduccion.Nesting/Core/
COPY GestionProduccion.Grading/Core/ GestionProduccion.Grading/Core/
COPY GestionProduccion.Application/ GestionProduccion.Application/
COPY GestionProduccion.Grading/Persistence/ GestionProduccion.Grading/Persistence/
COPY GestionProduccion.Grading/Worker/ GestionProduccion.Grading/Worker/
COPY appsettings.GradingMeasuredAlpha.json ./
RUN --mount=type=cache,id=gestionproduccion-nuget-v2,target=/root/.nuget/packages,sharing=locked \
    dotnet publish GestionProduccion.Grading/Worker/GestionProduccion.Grading.Worker.csproj \
    -c Release -o /worker -p:UseAppHost=false -m:1 -nr:false -p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:f24d74e8185b14aba4c7563e059e0f526699d4730998a83515d3af03058e1266
WORKDIR /app
COPY --from=publish /worker/ ./
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "GestionProduccion.Grading.Worker.dll"]

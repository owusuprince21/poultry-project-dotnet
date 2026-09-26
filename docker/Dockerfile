FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG PROJECT
RUN dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ARG ASSEMBLY
ENV ASSEMBLY_NAME=${ASSEMBLY}
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ASSEMBLY_NAME\""]

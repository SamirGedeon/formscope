# Docker example

```sh
docker compose up --build
```

Open **http://localhost:5007**. The example uses the Development environment for local HTTP cookies and persists SQLite plus Data Protection keys in `formscope-data`. No account is seeded.

The Dockerfile uses Microsoft's .NET 10 SDK/ASP.NET images, restores the locked dependency graph, publishes the application, and runs as the base image's non-root application user. The image has not been built in this development environment; the native SDK build and browser flow were tested.

For public hosting, use Production with HTTPS, protect/persist the data volume and configure any TLS proxy trust deliberately. The example does not enable forwarded-header trust. Removing the volume deletes forms, responses, sessions and protection keys.

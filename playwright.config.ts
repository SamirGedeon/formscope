import { defineConfig } from "@playwright/test";
import { resolve } from "node:path";
export default defineConfig({
  testDir: "./tests/browser",
  workers: 1,
  reporter: "list",
  use: {
    baseURL: "http://localhost:5007",
    headless: true,
    launchOptions: {
      executablePath: process.env.PLAYWRIGHT_CHROMIUM_PATH || undefined,
    },
  },
  webServer: process.env.PLAYWRIGHT_EXTERNAL_SERVER
    ? undefined
    : {
        command:
          "dotnet run --project server --no-build --no-launch-profile --urls http://127.0.0.1:5007",
        url: "http://localhost:5007/api/health",
        reuseExistingServer: false,
        env: {
          DOTNET_ENVIRONMENT: "Development",
          DataPath: resolve("server/var/e2e"),
        },
      },
});

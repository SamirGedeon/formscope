import { test, expect } from "@playwright/test";
async function register(page: any) {
  await page.goto("/");
  await page
    .getByLabel("Email", { exact: true })
    .fill("learner." + crypto.randomUUID().slice(0, 8) + "@example.com");
  await page
    .getByLabel("Password", { exact: true })
    .fill("a-safe-browser-password");
  await page
    .getByRole("button", { name: "Create account", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: /A world of possibilities/ }),
  ).toBeVisible();
}
async function token(request: any) {
  return (await (await request.get("/api/session")).json()).csrf;
}
async function create(request: any, title = "Product feedback") {
  const csrf = await token(request);
  const r = await request.post("/api/forms", {
    headers: { "X-CSRF-TOKEN": csrf },
    data: {
      title,
      description: "Help us shape what comes next.",
      questions: [
        {
          id: "email",
          label: "Your email",
          kind: "email",
          required: true,
          options: [],
        },
        {
          id: "priority",
          label: "What should we improve?",
          kind: "choice",
          required: true,
          options: ["Onboarding", "Customization", "Performance"],
        },
        {
          id: "notes",
          label: "Anything else to share?",
          kind: "text",
          required: false,
          options: [],
        },
      ],
      version: 0,
    },
  });
  expect(r.status()).toBe(201);
  return r.json();
}
test("build, publish, collect a response, view answers and close the form", async ({
  page,
  browser,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await register(page);
  await page.getByRole("button", { name: "+ New form" }).click();
  await page.getByLabel("Form title").fill("Product feedback");
  await page
    .getByLabel("Description", { exact: true })
    .fill("A clearer view of what matters.");
  await page.getByRole("button", { name: "Create form", exact: true }).click();
  await page.getByRole("button", { name: "+ Add a question" }).click();
  await page.getByLabel("Question label").fill("Your email");
  await page.getByLabel("Question type", { exact: true }).selectOption("email");
  await page.getByRole("button", { name: "Save question" }).click();
  await expect(page.locator(".question-row")).toHaveCount(1);
  await page.getByRole("button", { name: "Publish form", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Close responses" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "+ Add a question" }),
  ).toHaveCount(0);
  const link = await page.getByLabel("Public form link").inputValue();
  const context = await browser.newContext();
  const visitor = await context.newPage();
  await visitor.goto(link);
  await visitor
    .getByLabel("Your email", { exact: false })
    .fill("reader@example.com");
  await visitor.getByRole("button", { name: "Send response" }).click();
  await expect(
    visitor.getByRole("heading", { name: "Thanks for sharing." }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Reload latest" }).click();
  await page.getByRole("button", { name: /^Responses/ }).click();
  await expect(
    page.getByText("reader@example.com", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: /^Questions/ }).click();
  await page.getByRole("button", { name: "Close responses" }).click();
  await visitor.goto(link);
  await expect(
    visitor.getByRole("heading", { name: "This form is unavailable." }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Reopen form" }).click();
  await visitor.reload();
  await expect(
    visitor.getByRole("button", { name: "Send response" }),
  ).toBeVisible();
  expect(errors).toEqual([]);
  await context.close();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Create account", exact: true }),
  ).toBeVisible();
});
test("real HTTP stack enforces CSRF, ownership and submission idempotency", async ({
  page,
  playwright,
}) => {
  await register(page);
  expect((await page.request.post("/api/forms", { data: {} })).status()).toBe(
    400,
  );
  const f = await create(page.request);
  const csrf = await token(page.request);
  expect(
    (
      await page.request.post(`/api/forms/${f.id}/state`, {
        headers: { "X-CSRF-TOKEN": csrf },
        data: { state: "published", version: 1 },
      })
    ).ok(),
  ).toBeTruthy();
  const other = await playwright.request.newContext({
    baseURL: "http://localhost:5007",
  });
  let t = await token(other);
  await other.post("/api/auth/signup", {
    headers: { "X-CSRF-TOKEN": t },
    data: {
      email: "other." + crypto.randomUUID().slice(0, 8) + "@example.com",
      password: "a-safe-browser-password",
    },
  });
  t = await token(other);
  expect((await other.get(`/api/forms/${f.id}`)).status()).toBe(404);
  expect((await other.get(`/api/forms/${f.id}/responses`)).status()).toBe(404);
  const body = {
    requestId: crypto.randomUUID(),
    answers: { email: "reader@example.com", priority: "Customization" },
  };
  for (let i = 0; i < 2; i++)
    expect(
      (
        await other.post(`/api/public/${f.id}/responses`, {
          headers: { "X-CSRF-TOKEN": t },
          data: body,
        })
      ).ok(),
    ).toBeTruthy();
  expect(
    (await (await page.request.get(`/api/forms/${f.id}/responses`)).json())
      .length,
  ).toBe(1);
  expect(
    (
      await other.post(`/api/public/${f.id}/responses`, {
        headers: { "X-CSRF-TOKEN": t },
        data: {
          requestId: crypto.randomUUID(),
          answers: { email: "reader@example.com", priority: "Unknown" },
        },
      })
    ).status(),
  ).toBe(400);
  await other.dispose();
});
test("dashboard, builder, public form and mobile screenshots", async ({
  page,
}) => {
  await register(page);
  const f = await create(page.request);
  await create(page.request, "A little onboarding feedback");
  await create(page.request, "Your next great idea");
  const csrf = await token(page.request);
  await page.request.post(`/api/forms/${f.id}/state`, {
    headers: { "X-CSRF-TOKEN": csrf },
    data: { state: "published", version: 1 },
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.reload();
  await expect(
    page.getByRole("heading", { name: /A world of possibilities/ }),
  ).toBeVisible();
  await page.screenshot({ path: "docs/dashboard.png", fullPage: true });
  await page.getByRole("button", { name: /Product feedback/ }).click();
  await page.screenshot({ path: "docs/builder.png", fullPage: true });
  await page.goto("/f/" + f.id);
  await expect(
    page.getByRole("button", { name: "Send response" }),
  ).toBeVisible();
  await page.screenshot({ path: "docs/form.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({ path: "docs/mobile.png", fullPage: true });
});

test("form labels and response text render as text rather than HTML", async ({
  page,
}) => {
  await register(page);
  const csrf = await token(page.request);
  const label = '<img src=x onerror="window.compromised=true">';
  const answer = '<svg onload="window.compromised=true">';
  const response = await page.request.post("/api/forms", {
    headers: { "X-CSRF-TOKEN": csrf },
    data: {
      title: "Literal <script> text",
      description: 'Quotes " and <tags> remain text.',
      questions: [
        { id: "text", label, kind: "text", required: true, options: [] },
      ],
      version: 0,
    },
  });
  expect(response.status()).toBe(201);
  const form = await response.json();
  await page.request.post(`/api/forms/${form.id}/state`, {
    headers: { "X-CSRF-TOKEN": csrf },
    data: { state: "published", version: 1 },
  });
  await page.goto("/f/" + form.id);
  await expect(
    page.getByRole("heading", { name: "Literal <script> text", exact: true }),
  ).toBeVisible();
  expect(await page.locator("img, svg").count()).toBe(0);
  await page.getByRole("textbox").fill(answer);
  await page.getByRole("button", { name: "Send response" }).click();
  await expect(
    page.getByRole("heading", { name: "Thanks for sharing." }),
  ).toBeVisible();
  await page.goto("/");
  await page.getByRole("button", { name: /Literal/ }).click();
  await page.getByRole("button", { name: /^Responses/ }).click();
  await expect(page.getByText(answer, { exact: true })).toBeVisible();
  expect(await page.locator("img, svg").count()).toBe(0);
  expect(await page.evaluate(() => Object.hasOwn(window, "compromised"))).toBe(
    false,
  );
});

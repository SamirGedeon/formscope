const root = document.querySelector("#app");
let user = null,
  csrf = "",
  forms = [],
  selected = null,
  responses = [],
  tab = "builder",
  adding = false,
  creating = false,
  signup = true,
  busy = false,
  publicForm = null,
  submitted = false;
let requestId = crypto.randomUUID();
const h = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
async function api(path, body, method) {
  const r = await fetch("/api" + path, {
    method: method || (body === undefined ? "GET" : "POST"),
    headers:
      body === undefined
        ? {}
        : { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data;
  try {
    data = await r.json();
  } catch {
    data = {
      message:
        r.status === 429
          ? "Too many requests. Please try again later."
          : "Request failed. Reload the page and try again.",
    };
  }
  if (!r.ok) throw new Error(data.message || "Request failed.");
  return data;
}
async function session() {
  const s = await api("/session");
  user = s.user;
  csrf = s.csrf;
}
function notify(message, error = false) {
  const box = document.createElement("div");
  box.className = "toast" + (error ? " error" : "");
  box.setAttribute("role", error ? "alert" : "status");
  box.textContent = message;
  document.querySelector("#notifications").replaceChildren(box);
  setTimeout(() => box.remove(), 6500);
}
async function task(fn) {
  if (busy) return;
  busy = true;
  root.querySelectorAll("button").forEach((b) => (b.disabled = true));
  try {
    await fn();
  } catch (e) {
    notify(e.message, true);
  } finally {
    busy = false;
    root
      .querySelectorAll("button")
      .forEach((b) => (b.disabled = b.dataset.locked === "true"));
  }
}
const brand = `<a class="brand" href="/"><span class="logo"><i></i><i></i><i></i></span>Formscope</a>`;
const badge = (f) =>
  `<span class="badge ${h(f.state)}"><i></i>${h(f.state[0].toUpperCase() + f.state.slice(1))}</span>`;
const field = (q) =>
  `<label>${h(q.label)} ${q.required ? '<span class="required">*</span>' : "<small>optional</small>"}${q.kind === "choice" ? `<select name="${h(q.id)}" ${q.required ? "required" : ""}><option value="">Choose an option</option>${q.options.map((o) => `<option value="${h(o)}">${h(o)}</option>`).join("")}</select>` : `<${q.kind === "text" ? "textarea" : "input"} name="${h(q.id)}" ${q.kind === "email" ? 'type="email" maxlength="254"' : 'maxlength="2000"'} ${q.required ? "required" : ""} ${q.kind === "text" ? 'rows="3"' : ""}>${q.kind === "text" ? "</textarea>" : ""}`}</label>`;
function render() {
  if (location.pathname.startsWith("/f/")) return renderPublic();
  if (!user) {
    root.innerHTML = `<main class="entry"><section class="pitch">${brand}<div class="eyebrow">TURN A LITTLE CURIOSITY INTO CLARITY.</div><h1>Ask better.<br><em>Learn more.</em></h1><p>Create thoughtful forms, share them with your audience, and bring every response into one clear workspace.</p><div class="sample"><span>YOUR NEXT GOOD QUESTION</span><h3>What should we improve next?</h3><div class="sample-choice"><i></i>A simpler onboarding experience</div><div class="sample-choice checked"><i></i>More ways to make it my own</div><div class="sample-choice"><i></i>A little more of everything</div><footer>GOOD QUESTIONS OPEN NEW POSSIBILITIES.</footer></div><div class="pitch-tags"><span>Build simply</span><span>Share openly</span><span>Learn together</span></div></section><section class="panel auth"><span class="eyebrow">YOUR PERSONAL FORM WORKSPACE</span><h2>${signup ? "Make space for better answers." : "Welcome back."}</h2><p>${signup ? "Create an account and start the conversation." : "Your next insight is waiting for you."}</p><form data-form="auth"><label>Email<input name="email" type="email" autocomplete="email" required maxlength="254" placeholder="you@example.com"></label><label>Password<input name="password" type="password" autocomplete="${signup ? "new-password" : "current-password"}" required minlength="10" maxlength="128" placeholder="At least 10 characters"></label><button class="primary wide">${signup ? "Create account" : "Sign in"}</button></form><button class="text auth-toggle" data-action="toggle-auth">${signup ? "Already have an account? Sign in" : "New here? Create an account"}</button><p class="fine">Your forms and responses stay in your workspace.</p></section></main>`;
    return;
  }
  const total = forms.reduce((n, f) => n + f.responses, 0),
    published = forms.filter((f) => f.state === "published").length;
  root.innerHTML = `<div class="layout"><aside>${brand}<div class="workspace-chip"><span class="avatar">P</span><div>Personal workspace<small>YOUR NEXT INSIGHT STARTS HERE</small></div></div><div class="nav-caption">WORKSPACE</div><button class="nav active" data-action="home"><span>▦</span> My forms <b>${forms.length}</b></button><div class="aside-tip"><span class="tip-symbol">?</span><strong>Every answer starts somewhere.</strong><p>A thoughtful question makes room for a useful conversation.</p></div><div class="profile"><span class="avatar">${h(user.email[0].toUpperCase())}</span><div><span>${h(user.email)}</span><button class="text" data-action="logout">Sign out</button></div></div></aside><main class="workspace"><header><span>PERSONAL WORKSPACE <b>/</b> ${selected ? h(selected.title) : "FORM OVERVIEW"}</span><span class="header-pill"><i></i>A little curiosity goes a long way</span></header>${selected ? editor() : `<section class="page-heading"><div><div class="eyebrow">OPEN THE CONVERSATION</div><h1>Your questions.<br class="mobile-break"> A world of possibilities.</h1><p>A simple home for the things you want to ask.</p></div><button class="primary" data-action="new-form">+ New form</button></section><section class="metrics"><div><span>YOUR FORMS</span><strong>${forms.length}<small>ideas ready to take shape</small></strong></div><div><span>LIVE FORMS</span><strong>${published}<small>open for responses</small></strong></div><div><span>RESPONSES</span><strong>${total}<small>new perspectives to explore</small></strong></div></section>${creating ? `<form class="panel create-form" data-form="create"><h2>What would you like to ask?</h2><label>Form title<input name="title" required maxlength="100" placeholder="e.g. Product feedback"></label><label>Description<textarea name="description" maxlength="400" placeholder="A little context for your audience."></textarea></label><div class="form-actions"><button class="text" type="button" data-action="cancel-create">Cancel</button><button class="primary">Create form</button></div></form>` : ""}<section class="section-heading"><h2>Your forms <span>${forms.length}</span></h2><span>Good questions, all in one place</span></section><section class="form-grid">${forms.length ? forms.map((f) => `<button class="panel form-card" data-action="open" data-id="${h(f.id)}"><div class="card-top"><span class="form-icon">▤</span>${badge(f)}</div><h3>${h(f.title)}</h3><p>${h(f.description || "A new conversation, ready to take shape.")}</p><div class="card-stats"><span><b>${f.questions.length}</b> questions</span><span><b>${f.responses}</b> responses</span></div><div class="card-footer">Open form <span>→</span></div></button>`).join("") : `<div class="panel empty"><span>+</span><h3>Start with a good question.</h3><p>Create your first form and make room for a new perspective.</p></div>`}</section><section class="guide"><span>01</span><div><h3>A thoughtful form feels like a conversation.</h3><p>Keep questions focused, add a little context, and make it easy for people to share what matters.</p></div></section>`}<footer class="page-footer"><span>Good questions. Clearer answers.</span><span>Formscope / 01</span></footer></main></div>`;
}
function editor() {
  const f = selected;
  return `<button class="text back" data-action="home">← All forms</button><section class="detail-heading"><div><span class="eyebrow">MAKE THE CONVERSATION YOURS</span><h1>${h(f.title)}</h1><p>${h(f.description || "A new perspective starts with a clear question.")}</p></div>${badge(f)}</section><div class="editor-toolbar"><div class="tabs"><button data-action="builder" class="${tab === "builder" ? "active" : ""}">Questions <span>${f.questions.length}</span></button><button data-action="responses" class="${tab === "responses" ? "active" : ""}">Responses <span>${f.responses}</span></button></div><button class="text" data-action="reload">Reload latest</button></div>${tab === "responses" ? `<section class="panel response-panel"><div class="panel-heading"><h2>What your audience shared</h2><span>Latest ${responses.length} responses</span></div>${responses.length ? responses.map((r, i) => `<article class="response-card"><div class="response-header"><strong>Response ${responses.length - i}</strong><time>${new Date(r.created).toLocaleString()}</time></div>${f.questions.map((q) => `<div class="answer"><span>${h(q.label)}</span><p>${h(r.answers[q.id] || "No answer")}</p></div>`).join("")}</article>`).join("") : `<div class="empty"><h3>A little patience, a new perspective.</h3><p>${f.state === "draft" ? "Publish your form to start collecting responses." : "Share your public link to invite the first response."}</p></div>`}</section>` : `<div class="detail-grid"><section class="main-column"><section class="panel questions-panel"><div class="panel-heading"><h2>Your questions</h2><span>${f.questions.length} / 10</span></div>${f.questions.length ? f.questions.map((q, i) => `<article class="question-row"><span class="question-number">${String(i + 1).padStart(2, "0")}</span><div><h3>${h(q.label)}</h3><p>${h(q.kind === "choice" ? "Multiple choice" : q.kind === "email" ? "Email address" : "Long answer")} · ${q.required ? "Required" : "Optional"}</p>${q.kind === "choice" ? `<div class="option-tags">${q.options.map((o) => `<span>${h(o)}</span>`).join("")}</div>` : ""}</div>${f.state === "draft" ? `<button class="remove" data-action="remove-question" data-id="${h(q.id)}" aria-label="Remove ${h(q.label)}">×</button>` : ""}</article>`).join("") : `<div class="empty"><span>?</span><h3>Your first question is waiting.</h3><p>Ask something clear, useful, and easy to answer.</p></div>`}${f.state === "draft" && f.questions.length < 10 ? `<button class="add-question" data-action="add-question">+ Add a question</button>` : ""}</section>${adding ? `<form class="panel question-form" data-form="question"><h2>A little curiosity, a new question.</h2><label>Question label<input name="label" required maxlength="100" placeholder="What would you like to know?"></label><label for="kind">Question type</label><select name="kind" id="kind"><option value="text">Long answer</option><option value="email">Email address</option><option value="choice">Multiple choice</option></select><label class="checkbox"><input name="required" type="checkbox" checked> An answer is required</label><label id="options-label" hidden>Options <small>One per line, 2–8 options</small><textarea name="options" maxlength="488" placeholder="First option&#10;Second option"></textarea></label><div class="form-actions"><button class="text" type="button" data-action="cancel-question">Cancel</button><button class="primary">Save question</button></div></form>` : ""}</section><div class="side-column"><section class="panel publish-panel"><span class="eyebrow">THE NEXT STEP</span><h2>${f.state === "draft" ? "Ready for a new perspective?" : f.state === "published" ? "The conversation is open." : "Taking a little pause."}</h2><p>${f.state === "draft" ? "Publish your form when the questions feel right. After publishing, questions can no longer be edited." : f.state === "published" ? "Your form is accepting responses. Share the link with the people you want to hear from." : "Responses are paused. You can reopen the same form whenever you are ready."}</p>${f.state !== "draft" ? `<label>Public form link<input class="share-link" readonly value="${h(location.origin + "/f/" + f.id)}"></label><a class="secondary wide center" href="/f/${h(f.id)}">View public form →</a>` : ""}<button class="primary wide" data-action="state" data-state="${f.state === "published" ? "closed" : "published"}">${f.state === "draft" ? "Publish form" : f.state === "published" ? "Close responses" : "Reopen form"}</button><p class="fine">${f.state === "draft" ? "Add at least one question before publishing." : "Your existing responses stay in your workspace."}</p></section><section class="explanation"><span>i</span><h3>Keep every answer meaningful.</h3><p>Questions stay fixed after publishing, so your responses always refer to the same form.</p><p>Need a different set of questions? Start a new form.</p></section><div class="version-note">Saved form · revision ${f.version}</div></div></div>`}`;
}
function renderPublic() {
  root.innerHTML = `<main class="public-page"><header class="public-header">${brand}<span>ONE FORM. A NEW PERSPECTIVE.</span></header>${submitted ? `<section class="panel thank-you"><span>✓</span><h1>Thanks for sharing.</h1><p>Your response has been received.</p></section>` : publicForm ? `<section class="public-heading"><span class="eyebrow">YOUR PERSPECTIVE MATTERS</span><h1>${h(publicForm.title)}</h1><p>${h(publicForm.description)}</p></section><form class="panel public-form" data-form="public">${publicForm.questions.map(field).join("")}<button class="primary wide">Send response</button><p class="fine">Your answers are shared with the form owner.</p></form>` : `<section class="panel thank-you"><span>—</span><h1>This form is unavailable.</h1><p>It may be closed or the link may be incorrect.</p></section>`}<footer class="public-footer">Thoughtful questions. A clearer view. <a href="/">Powered by Formscope</a></footer></main>`;
}
async function loadForm(id) {
  selected = await api("/forms/" + id);
  adding = false;
  responses = [];
}
async function refresh() {
  forms = await api("/forms");
}
root.addEventListener("change", (event) => {
  if (event.target.id === "kind") {
    document.querySelector("#options-label").hidden =
      event.target.value !== "choice";
  }
});
root.addEventListener("submit", (event) => {
  const form = event.target.closest("form");
  if (!form) return;
  event.preventDefault();
  const values = Object.fromEntries(new FormData(form));
  task(async () => {
    switch (form.dataset.form) {
      case "auth":
        await api("/auth/" + (signup ? "signup" : "login"), values);
        await session();
        await refresh();
        render();
        break;
      case "create":
        selected = await api("/forms", {
          title: values.title,
          description: values.description,
          questions: [],
          version: 0,
        });
        creating = false;
        await refresh();
        render();
        break;
      case "question": {
        const q = {
          id: crypto.randomUUID(),
          label: values.label,
          kind: values.kind,
          required: values.required === "on",
          options:
            values.kind === "choice"
              ? String(values.options)
                  .split("\n")
                  .map((s) => s.trim())
                  .filter(Boolean)
              : [],
        };
        selected = await api(
          "/forms/" + selected.id,
          {
            title: selected.title,
            description: selected.description,
            questions: [...selected.questions, q],
            version: selected.version,
          },
          "PUT",
        );
        adding = false;
        await refresh();
        render();
        notify("Question saved.");
        break;
      }
      case "public":
        await api("/public/" + publicForm.id + "/responses", {
          requestId,
          answers: values,
        });
        submitted = true;
        renderPublic();
        break;
    }
  });
});
root.addEventListener("click", (event) => {
  const button = event.target.closest("[data-action]");
  if (!button || busy) return;
  const action = button.dataset.action;
  task(async () => {
    switch (action) {
      case "toggle-auth":
        signup = !signup;
        render();
        break;
      case "home":
        selected = null;
        creating = false;
        adding = false;
        await refresh();
        render();
        break;
      case "logout":
        await api("/logout", {});
        await session();
        selected = null;
        forms = [];
        render();
        break;
      case "new-form":
        creating = true;
        render();
        document.querySelector("[name=title]").focus();
        break;
      case "cancel-create":
        creating = false;
        render();
        break;
      case "open":
        await loadForm(button.dataset.id);
        tab = "builder";
        render();
        break;
      case "reload": {
        const id = selected.id;
        await loadForm(id);
        await refresh();
        if (tab === "responses")
          responses = await api("/forms/" + id + "/responses");
        render();
        break;
      }
      case "builder":
        tab = "builder";
        render();
        break;
      case "responses":
        responses = await api("/forms/" + selected.id + "/responses");
        tab = "responses";
        render();
        break;
      case "add-question":
        adding = true;
        render();
        document.querySelector("[name=label]").focus();
        break;
      case "cancel-question":
        adding = false;
        render();
        break;
      case "remove-question":
        selected = await api(
          "/forms/" + selected.id,
          {
            title: selected.title,
            description: selected.description,
            version: selected.version,
            questions: selected.questions.filter(
              (q) => q.id !== button.dataset.id,
            ),
          },
          "PUT",
        );
        await refresh();
        render();
        break;
      case "state":
        selected = await api("/forms/" + selected.id + "/state", {
          state: button.dataset.state,
          version: selected.version,
        });
        adding = false;
        await refresh();
        render();
        notify("Form status updated.");
        break;
    }
  });
});
await session();
if (location.pathname.startsWith("/f/")) {
  try {
    publicForm = await api("/public/" + location.pathname.split("/")[2]);
  } catch {}
  renderPublic();
} else {
  if (user) await refresh();
  render();
}

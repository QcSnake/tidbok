// Bokningssidan. Ingen ramverkskod: fyra steg, ett litet tillstånd och fetch mot /api.
// All text från servern sätts med textContent, aldrig innerHTML.
(() => {
  "use strict";

  const slug = decodeURIComponent(location.pathname.split("/")[2] || "");
  const params = new URLSearchParams(location.search);
  const embedded = params.has("inbaddad");
  const $ = (id) => document.getElementById(id);
  const WINDOW = 7;

  const state = { business: null, service: null, staffId: null, staffName: null, from: null, day: null, time: null, slots: [] };

  // ---------- hjälpare ----------

  function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(props)) {
      if (v === null || v === undefined || v === false) continue;
      if (k === "class") node.className = v;
      else if (k === "text") node.textContent = v;
      else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
      else node.setAttribute(k, v === true ? "" : v);
    }
    for (const c of children) if (c !== null && c !== undefined) node.append(c);
    return node;
  }

  const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const parse = (s) => { const [y, m, d] = s.split("-").map(Number); return new Date(y, m - 1, d); };
  const addDays = (s, n) => { const d = parse(s); d.setDate(d.getDate() + n); return iso(d); };
  const fmt = (s, opts) => new Intl.DateTimeFormat("sv-SE", opts).format(parse(s));
  const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);
  const longDay = (s) => cap(fmt(s, { weekday: "long", day: "numeric", month: "long" }));
  const minutes = (m) => m < 60 ? `${m} min` : m % 60 === 0 ? `${m / 60} tim` : `${Math.floor(m / 60)} tim ${m % 60} min`;

  async function api(path, options) {
    const res = await fetch(path, { headers: { "Accept": "application/json", "Content-Type": "application/json" }, ...options });
    let body = null;
    try { body = await res.json(); } catch { /* tomt svar */ }
    return { ok: res.ok, status: res.status, body };
  }

  function showError(text) {
    const box = $("error");
    box.textContent = text || "";
    box.hidden = !text;
    if (text) box.scrollIntoView({ block: "nearest" });
  }

  function post(message) {
    if (embedded && window.parent !== window) window.parent.postMessage({ source: "tidbok", ...message }, "*");
  }

  // ---------- steg ----------

  const ORDER = ["service", "staff", "time", "details", "done"];

  function go(step) {
    showError("");
    for (const s of ORDER) $(`step-${s}`).hidden = s !== step;
    const current = ORDER.indexOf(step);
    document.querySelectorAll("#steps li").forEach((li, i) => {
      li.classList.toggle("is-done", i < current);
      li.classList.toggle("is-current", i === current);
      if (i === current) li.setAttribute("aria-current", "step"); else li.removeAttribute("aria-current");
    });
    const heading = $(`step-${step}`).querySelector("h2");
    if (heading) { heading.setAttribute("tabindex", "-1"); heading.focus({ preventScroll: true }); }
    window.scrollTo({ top: 0 });
    summary();
  }

  function summary() {
    const set = (id, text) => { const n = $(id); n.textContent = text || "Inte vald"; n.classList.toggle("is-empty", !text); };
    set("s-service", state.service ? `${state.service.name}, ${minutes(state.service.minutes)}${state.service.price ? `, ${state.service.price}` : ""}` : null);
    set("s-staff", state.service ? (state.staffName || (state.staffId === null && ORDER.indexOf(currentStep()) > 1 ? "Första lediga" : null)) : null);
    set("s-day", state.day ? longDay(state.day) : null);
    set("s-time", state.time);
  }

  const currentStep = () => ORDER.find((s) => !$(`step-${s}`).hidden) || "service";

  // ---------- 1. tjänst ----------

  function renderServices() {
    const list = $("services");
    list.replaceChildren(...state.business.services.map((s) => el("li", {},
      el("button", {
        class: "option", type: "button", "aria-pressed": String(state.service?.id === s.id),
        onclick: () => { state.service = s; state.staffId = null; state.staffName = null; state.day = null; state.time = null; renderStaff(); go("staff"); }
      },
        el("span", { class: "option__name", text: s.name }),
        el("span", { class: "option__price", text: s.price }),
        el("span", { class: "option__desc", text: s.description }),
        el("span", { class: "option__time", text: minutes(s.minutes) })
      ))));
  }

  // ---------- 2. vem ----------

  function renderStaff() {
    const people = state.business.staff.filter((p) => state.service.staffIds.includes(p.id));
    const choose = (id, name) => { state.staffId = id; state.staffName = name; state.day = null; state.time = null; startTime(); };

    const items = [el("li", {}, el("button", { class: "option", type: "button", onclick: () => choose(null, null) },
      el("span", { class: "option__name", text: "Första lediga" }),
      el("span", { class: "option__price", text: "" }),
      el("span", { class: "option__desc", text: people.length > 1 ? "Visar allas lediga tider." : "Visar alla lediga tider." })
    ))];

    for (const p of people) {
      items.push(el("li", {}, el("button", {
        class: "option", type: "button", "aria-pressed": String(state.staffId === p.id), onclick: () => choose(p.id, p.name)
      },
        el("span", { class: "option__name", text: p.name }),
        el("span", { class: "option__price", text: "" }),
        el("span", { class: "option__desc", text: p.title })
      )));
    }
    $("staff").replaceChildren(...items);
  }

  // ---------- 3. tid ----------

  function startTime() {
    go("time");
    state.from = state.from && state.from >= today() ? state.from : today();
    loadDays(true);
  }

  let todayIso = iso(new Date());
  const today = () => todayIso;

  async function loadDays(findFirstFree) {
    const days = $("days");
    days.replaceChildren(...Array.from({ length: WINDOW }, () => el("span", { class: "day", "aria-hidden": "true" })));
    $("slots").replaceChildren();
    $("dayhead").textContent = "";

    const q = new URLSearchParams({ service: state.service.id, from: state.from, count: WINDOW });
    if (state.staffId !== null) q.set("staff", state.staffId);
    const res = await api(`/api/businesses/${encodeURIComponent(slug)}/days?${q}`);
    if (!res.ok) { showError(res.body?.message || "Kunde inte hämta lediga dagar."); return; }

    const list = res.body;
    const horizonEnd = addDays(today(), state.business.horizonDays);

    // Inget ledigt i veckan: bläddra fram automatiskt första gången, men inte förbi horisonten.
    if (findFirstFree && !list.some((d) => d.free > 0) && addDays(state.from, WINDOW) <= horizonEnd) {
      state.from = addDays(state.from, WINDOW);
      return loadDays(true);
    }

    renderDays(list, horizonEnd);
    const pick = list.find((d) => d.date === state.day && d.free > 0) || list.find((d) => d.free > 0);
    if (pick) selectDay(pick.date);
    else { $("dayhead").textContent = "Inga lediga tider de här dagarna."; }
  }

  function renderDays(list, horizonEnd) {
    const max = Math.max(1, ...list.map((d) => d.free));
    $("range").textContent = `${cap(fmt(list[0].date, { day: "numeric", month: "short" }))} till ${fmt(list[list.length - 1].date, { day: "numeric", month: "short" })}`;
    $("prev").disabled = state.from <= today();
    $("next").disabled = addDays(state.from, WINDOW) > horizonEnd;

    $("days").replaceChildren(...list.map((d) => {
      const closed = d.free === 0;
      const label = `${longDay(d.date)}, ${closed ? (d.closed || "inga tider") : `${d.free} lediga tider`}`;
      const bar = el("i");
      bar.style.setProperty("--fill", `${Math.round((d.free / max) * 100)}%`);
      return el("button", {
        class: "day", type: "button", disabled: closed, "aria-label": label, title: label,
        "aria-pressed": String(d.date === state.day), "data-date": d.date,
        onclick: () => selectDay(d.date)
      },
        el("span", { class: "day__wd", text: fmt(d.date, { weekday: "short" }).replace(".", "") }),
        el("span", { class: "day__n", text: String(parse(d.date).getDate()) }),
        el("span", { class: "day__m", text: closed ? short(d.closed) : fmt(d.date, { month: "short" }).replace(".", "") }),
        el("span", { class: "day__bar", "aria-hidden": "true" }, bar));
    }));
  }

  // Etiketten under datumet har plats för ett ord. Hela skälet står i knappens titel och aria-label.
  const short = (reason) => {
    if (!reason) return "";
    if (reason === "Inga fler tider i dag") return "Slut";
    if (reason === "Går inte att boka än") return "Senare";
    if (["Stängt", "Fullbokat", "Passerat"].includes(reason)) return reason;
    return "Röd dag";
  };

  async function selectDay(date) {
    state.day = date;
    state.time = null;
    document.querySelectorAll("#days .day").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.date === date)));
    summary();

    const slots = $("slots");
    slots.replaceChildren(el("div", { class: "skeleton", "aria-label": "Laddar tider" }, el("i"), el("i")));
    const q = new URLSearchParams({ service: state.service.id, date });
    if (state.staffId !== null) q.set("staff", state.staffId);
    const res = await api(`/api/businesses/${encodeURIComponent(slug)}/slots?${q}`);
    if (state.day !== date) return; // användaren hann klicka på en annan dag
    if (!res.ok) { showError(res.body?.message || "Kunde inte hämta tiderna."); slots.replaceChildren(); return; }

    state.slots = res.body;
    const head = $("dayhead");
    head.replaceChildren(document.createTextNode(longDay(date) + " "), el("span", { text: `${res.body.length} lediga tider` }));

    if (res.body.length === 0) {
      slots.replaceChildren(el("p", { class: "slots__empty", text: "Tiderna den här dagen hann bli bokade. Välj en annan dag." }));
      return;
    }
    slots.replaceChildren(...res.body.map((s) => el("button", {
      class: "slot", type: "button", "aria-pressed": "false", text: s.time,
      "aria-label": `${s.time}, ${longDay(date)}`,
      onclick: () => { state.time = s.time; go("details"); $("form").elements.name.focus(); }
    })));
    post({ type: "tidbok:height", height: document.documentElement.scrollHeight });
  }

  // ---------- 4. uppgifter ----------

  async function submit(e) {
    e.preventDefault();
    const form = $("form");
    form.querySelectorAll("[data-field]").forEach((f) => { f.classList.remove("has-error"); f.querySelector(".field__error")?.remove(); });

    const data = Object.fromEntries(new FormData(form));
    const body = {
      serviceId: state.service.id, staffId: state.staffId, start: `${state.day}T${state.time}`,
      name: data.name, phone: data.phone, email: data.email || null, note: data.note || null,
      consent: data.consent === "true"
    };

    const button = $("submit");
    button.disabled = true;
    button.textContent = "Bokar";
    const res = await api(`/api/businesses/${encodeURIComponent(slug)}/bookings`, { method: "POST", body: JSON.stringify(body) });
    button.disabled = false;
    button.textContent = "Boka tiden";

    if (res.status === 201) return done(res.body);

    if (res.status === 400 && res.body?.fields) {
      let first = null;
      for (const [field, message] of Object.entries(res.body.fields)) {
        const wrap = form.querySelector(`[data-field="${CSS.escape(field)}"]`);
        if (!wrap) continue;
        wrap.classList.add("has-error");
        wrap.append(el("p", { class: "field__error", text: message }));
        first = first || wrap.querySelector("input, textarea");
      }
      showError(res.body.message);
      first?.focus();
      return;
    }

    if (res.status === 409) {
      go("time");
      showError(res.body?.message || "Tiden hann bli bokad. Välj en annan.");
      loadDays(false);
      return;
    }

    showError(res.status === 429
      ? "För många bokningar på kort tid från samma nätverk. Vänta en minut."
      : res.body?.message || "Något gick fel. Försök igen.");
  }

  function done(b) {
    $("done-text").textContent = `${b.service} hos ${b.staff}, ${b.dateText.toLowerCase()} kl ${b.timeText.split("-")[0]}. ${b.address}`;
    $("done-ref").textContent = b.reference;
    $("done-ics").href = b.calendarUrl;
    $("done-ics").setAttribute("download", `${b.reference}.ics`);
    $("done-manage").href = b.manageUrl;
    $("done-manage").target = embedded ? "_blank" : "_self";
    $("done-link").textContent = b.manageUrl;
    $("form").reset();
    go("done");
    $("step-done").focus();
    post({ type: "tidbok:booked", reference: b.reference });
  }

  // ---------- start ----------

  async function init() {
    if (embedded) document.body.classList.add("is-embedded");
    $("tema").href = `/tema/${encodeURIComponent(slug)}.css`;

    const res = await api(`/api/businesses/${encodeURIComponent(slug)}`);
    if (!res.ok) { $("b-name").textContent = "Verksamheten finns inte"; showError(res.body?.message); return; }

    const b = state.business = res.body;
    document.title = `Boka tid · ${b.name}`;
    $("b-kind").textContent = b.kind;
    $("b-name").textContent = b.name;
    $("b-meta").replaceChildren(document.createTextNode(`${b.address} · `),
      el("a", { href: `tel:${b.phone.replace(/[^0-9+]/g, "")}`, text: b.phone }));
    $("s-policy").textContent = `Avboka själv fram till ${b.cancelCutoffHours} timmar innan. Senare än så, ring ${b.phone}.`;

    renderServices();
    go("service");

    $("prev").addEventListener("click", () => { state.from = addDays(state.from, -WINDOW); if (state.from < today()) state.from = today(); loadDays(false); });
    $("next").addEventListener("click", () => { state.from = addDays(state.from, WINDOW); loadDays(false); });
    document.querySelectorAll("[data-back]").forEach((b) => b.addEventListener("click", () => {
      const to = b.dataset.back;
      if (to === "service") renderServices();
      if (to === "staff") renderStaff();
      if (to === "time") { go("time"); loadDays(false); return; }
      go(to);
    }));
    $("form").addEventListener("submit", submit);
    $("again").addEventListener("click", () => { state.service = null; state.day = null; state.time = null; renderServices(); go("service"); });
    $("close").addEventListener("click", () => post({ type: "tidbok:close" }));
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") post({ type: "tidbok:close" }); });

    if ("ResizeObserver" in window) new ResizeObserver(() => post({ type: "tidbok:height", height: document.documentElement.scrollHeight })).observe(document.body);
  }

  init();
})();

// Sidan bakom länken i bokningsbekräftelsen: visa bokningen, lägg in i kalendern, avboka.
(() => {
  "use strict";

  const token = decodeURIComponent(location.pathname.split("/")[2] || "");
  const $ = (id) => document.getElementById(id);

  async function api(path, options) {
    const res = await fetch(path, { headers: { "Accept": "application/json" }, ...options });
    let body = null;
    try { body = await res.json(); } catch { /* tomt */ }
    return { ok: res.ok, status: res.status, body };
  }

  function error(text) {
    $("error").textContent = text;
    $("error").hidden = !text;
  }

  function render(b) {
    $("tema").href = `/tema/${encodeURIComponent(b.businessSlug)}.css`;
    document.title = `${b.reference} · ${b.business}`;
    $("m-business").textContent = b.business;
    $("m-title").textContent = b.status === "cancelled" ? "Bokningen är avbokad" : (b.firstName ? `Hej ${b.firstName}` : "Din bokning");
    $("m-status").textContent = b.status === "cancelled" ? "Avbokad" : "Bokad";
    $("m-status").className = `status status--${b.status}`;
    $("m-service").textContent = b.price ? `${b.service}, ${b.price}` : b.service;
    $("m-day").textContent = b.dateText;
    $("m-time").textContent = b.timeText;
    $("m-staff").textContent = b.staff;
    $("m-address").textContent = b.address;
    $("m-ref").textContent = b.reference;
    $("m-ics").href = b.calendarUrl;
    $("m-ics").setAttribute("download", `${b.reference}.ics`);
    $("m-ics").parentElement.hidden = b.status === "cancelled";
    $("m-again").href = `/boka/${encodeURIComponent(b.businessSlug)}`;

    $("m-cancel").hidden = !b.canCancel;
    const late = b.status === "booked" && !b.canCancel && b.cancelBlockedReason;
    $("m-late").hidden = !late;
    if (late) $("m-late").textContent = `${b.cancelBlockedReason} Telefon: ${b.phone}.`;
    $("m-card").hidden = false;
  }

  async function init() {
    const res = await api(`/api/bookings/${encodeURIComponent(token)}`);
    if (!res.ok) {
      $("m-title").textContent = "Bokningen hittades inte";
      error("Länken stämmer inte, eller så har bokningen tagits bort. Kontrollera att hela länken kom med.");
      return;
    }
    render(res.body);

    $("m-confirm").addEventListener("click", async () => {
      $("m-confirm").disabled = true;
      const r = await api(`/api/bookings/${encodeURIComponent(token)}/cancel`, { method: "POST" });
      $("m-confirm").disabled = false;
      if (r.ok) { error(""); render(r.body); $("m-title").focus(); return; }
      error(r.body?.message || "Det gick inte att avboka. Ring oss.");
    });
    $("m-title").setAttribute("tabindex", "-1");
  }

  init();
})();

/*
  Tidbok-widgeten. Lägg in på valfri sajt:

    <script src="https://tidbok.onrender.com/widget.js" data-tidbok="salong-silhuett" defer></script>
    <button type="button" data-tidbok-open>Boka tid</button>

  Alla element med data-tidbok-open öppnar bokningen ovanpå sidan. Utan något sådant element
  lägger widgeten till en egen knapp nere till höger. Stilarna sätts direkt på elementen, så
  widgeten fungerar även på sajter som inte tillåter inline-stilmallar.
*/
(() => {
  "use strict";

  const script = document.currentScript;
  if (!script) return;
  const slug = script.dataset.tidbok;
  if (!slug) { console.warn("Tidbok: data-tidbok saknas på script-taggen."); return; }

  const origin = new URL(script.src).origin;
  const src = `${origin}/boka/${encodeURIComponent(slug)}?inbaddad=1`;
  let overlay = null;
  let frame = null;
  let opener = null;

  function css(node, styles) { Object.assign(node.style, styles); return node; }

  function open(e) {
    if (e) e.preventDefault();
    opener = document.activeElement;
    if (!overlay) build();
    overlay.hidden = false;
    document.documentElement.style.overflow = "hidden";
    frame.focus();
  }

  function close() {
    if (!overlay) return;
    overlay.hidden = true;
    document.documentElement.style.overflow = "";
    if (opener && opener.focus) opener.focus();
  }

  function build() {
    overlay = css(document.createElement("div"), {
      position: "fixed", inset: "0", zIndex: "2147483000", background: "rgba(22,24,26,.55)",
      display: "flex", alignItems: "center", justifyContent: "center", padding: "16px"
    });
    overlay.setAttribute("role", "dialog");
    overlay.setAttribute("aria-modal", "true");
    overlay.setAttribute("aria-label", "Boka tid");
    overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });

    frame = css(document.createElement("iframe"), {
      width: "100%", maxWidth: "980px", height: "min(860px, calc(100vh - 32px))",
      border: "0", background: "#f6f5f1", boxShadow: "0 30px 80px rgba(0,0,0,.35)"
    });
    frame.src = src;
    frame.title = "Bokning";
    frame.setAttribute("allow", "clipboard-write");
    overlay.append(frame);
    document.body.append(overlay);
  }

  window.addEventListener("message", (e) => {
    if (e.origin !== origin || !e.data || e.data.source !== "tidbok") return;
    if (e.data.type === "tidbok:close") close();
  });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && overlay && !overlay.hidden) close(); });

  function wire() {
    const triggers = document.querySelectorAll("[data-tidbok-open]");
    triggers.forEach((t) => t.addEventListener("click", open));
    if (triggers.length > 0) return;

    const button = css(document.createElement("button"), {
      position: "fixed", right: "20px", bottom: "20px", zIndex: "2147482999",
      padding: "14px 20px", border: "0", background: "#16181a", color: "#fff",
      font: "600 15px/1 system-ui, sans-serif", cursor: "pointer"
    });
    button.type = "button";
    button.textContent = "Boka tid";
    button.addEventListener("click", open);
    document.body.append(button);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", wire);
  else wire();
})();

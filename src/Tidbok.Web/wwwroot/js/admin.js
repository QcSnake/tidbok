// Adminvyn. Servern räknar ut positionerna i dagsvyn och skickar dem som data-attribut.
// Här blir de CSS-variabler, eftersom CSP:n inte tillåter style-attribut i HTML:en.
(() => {
  "use strict";

  document.querySelectorAll("[data-top]").forEach((n) => n.style.setProperty("--top", `${n.dataset.top}%`));
  document.querySelectorAll("[data-h]").forEach((n) => n.style.setProperty("--h", `${n.dataset.h}%`));
  document.querySelectorAll("[data-hours]").forEach((n) => n.style.setProperty("--hours", n.dataset.hours));

  document.querySelectorAll("[data-autosubmit]").forEach((input) =>
    input.addEventListener("change", () => input.form && input.value && input.form.submit()));

  document.querySelectorAll("[data-copy]").forEach((button) =>
    button.addEventListener("click", async () => {
      const source = document.querySelector(button.dataset.copy);
      if (!source) return;
      const label = button.textContent;
      try {
        await navigator.clipboard.writeText(source.textContent.trim());
        button.textContent = "Kopierat";
      } catch {
        const range = document.createRange();
        range.selectNodeContents(source);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
        button.textContent = "Markerat, tryck Ctrl+C";
      }
      setTimeout(() => { button.textContent = label; }, 1800);
    }));
})();

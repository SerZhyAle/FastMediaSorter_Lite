// The live filter of the subject index (ticket L0301, DEF-05): it narrows the topic cards of topics.html and its language
// siblings to those whose title, summary or data-keywords hold every typed word. No network request, no dependency; the
// counter and empty-state words come from the page's own language (data-found on the search block). Without script the
// block stays hidden and the page is the full index.
(function () {
  var box = document.querySelector('[data-topics-search]');
  var input = document.getElementById('topics-q');
  var count = document.getElementById('topics-count');
  var none = document.getElementById('topics-none');
  if (!box || !input || !count || !none) return;
  var cards = Array.prototype.slice.call(document.querySelectorAll('.topics .cards > a'));
  var groups = Array.prototype.slice.call(document.querySelectorAll('.topics .topic-group'));
  var texts = cards.map(function (c) {
    return ((c.textContent || '') + ' ' + (c.getAttribute('data-keywords') || '')).toLocaleLowerCase();
  });
  function run() {
    var words = input.value.toLocaleLowerCase().split(/\s+/).filter(Boolean);
    var shown = 0;
    cards.forEach(function (c, i) {
      var hit = words.every(function (w) { return texts[i].indexOf(w) !== -1; });
      c.style.display = hit ? '' : 'none';
      if (hit) shown++;
    });
    groups.forEach(function (g) {
      var any = cards.some(function (c) { return c.style.display !== 'none' && g.contains(c); });
      g.style.display = any ? '' : 'none';
    });
    count.textContent = words.length ? (box.getAttribute('data-found') || '{0}').replace('{0}', String(shown)) : '';
    none.hidden = shown > 0;
  }
  input.addEventListener('input', run);
  box.hidden = false;
})();

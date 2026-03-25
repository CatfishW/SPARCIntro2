await page.waitForFunction(() => {
  const el = document.querySelector('#unity-loading-bar');
  return el && getComputedStyle(el).display === 'none';
}, { timeout: 180000 });
const canvas = document.querySelector('#unity-canvas');
const loadingBar = document.querySelector('#unity-loading-bar');
const warning = document.querySelector('#unity-warning');
return {
  title: document.title,
  canvasPresent: Boolean(canvas),
  canvasSize: canvas ? { width: canvas.width, height: canvas.height } : null,
  loadingDisplay: loadingBar ? getComputedStyle(loadingBar).display : 'missing',
  warningText: warning ? warning.innerText : '',
  bodyText: document.body.innerText.slice(0, 400)
};

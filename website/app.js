const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

const releaseFallback = 'https://github.com/PedroVitor-Dev/AniT/releases/latest';
const releaseLinks = [...document.querySelectorAll('.release-link')];
const releaseMetadata = [...document.querySelectorAll('.release-meta')];
const navVersion = document.querySelector('.nav-version');

async function resolveLatestRelease() {
  try {
    const response = await fetch('https://api.github.com/repos/PedroVitor-Dev/AniT/releases/latest', {
      headers: { Accept: 'application/vnd.github+json' }
    });
    if (!response.ok) throw new Error('Release indisponível');
    const release = await response.json();
    const installer = release.assets?.find(asset => /AniT-Setup-.*-win-x64\.exe$/i.test(asset.name));
    const destination = installer?.browser_download_url || release.html_url || releaseFallback;
    const version = String(release.tag_name || '').replace(/^v/i, '') || 'mais recente';
    const size = installer?.size ? `${(installer.size / 1024 / 1024).toFixed(0)} MB` : 'Windows x64';

    releaseLinks.forEach(link => {
      link.href = destination;
      if (installer) link.setAttribute('download', '');
    });
    releaseMetadata.forEach(item => { item.textContent = `Versão ${version} · ${size}`; });
    if (navVersion) navVersion.textContent = `v${version}`;
  } catch {
    releaseLinks.forEach(link => { link.href = releaseFallback; });
    releaseMetadata.forEach(item => { item.textContent = 'Versão mais recente · Windows x64'; });
  }
}

resolveLatestRelease();

const lightningInvoice = document.querySelector('#lightningInvoice')?.textContent.trim() || '';
const copyInvoice = document.querySelector('#copyInvoice');
const copyStatus = document.querySelector('#copyStatus');
const openLightningWallet = document.querySelector('#openLightningWallet');

if (openLightningWallet && lightningInvoice) {
  openLightningWallet.href = `lightning:${lightningInvoice}`;
}

copyInvoice?.addEventListener('click', async () => {
  if (!lightningInvoice) return;
  try {
    await navigator.clipboard.writeText(lightningInvoice);
    copyInvoice.innerHTML = '<span aria-hidden="true">✓</span> Invoice copiada';
    if (copyStatus) copyStatus.textContent = 'Pronto! Agora é só colar na sua carteira Lightning.';
  } catch {
    const selection = window.getSelection();
    const range = document.createRange();
    const invoiceElement = document.querySelector('#lightningInvoice');
    if (!selection || !invoiceElement) return;
    range.selectNodeContents(invoiceElement);
    selection.removeAllRanges();
    selection.addRange(range);
    if (copyStatus) copyStatus.textContent = 'Invoice selecionada. Use Ctrl+C para copiar.';
  }
});

const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  });
}, { threshold: 0.14 });

document.querySelectorAll('.reveal').forEach(element => observer.observe(element));

const topbar = document.querySelector('.topbar');
window.addEventListener('scroll', () => {
  topbar?.classList.toggle('scrolled', window.scrollY > 48);
}, { passive: true });

const moodAssets = {
  normal: './assets/baki-normal.png',
  happy: './assets/baki-happy.png',
  focused: './assets/baki-angry.png',
  sleepy: './assets/baki-sleepy.png'
};

const expression = document.querySelector('#bakiExpression');
const line = document.querySelector('#bakiLine');
document.querySelectorAll('.mood').forEach(button => {
  button.addEventListener('click', () => {
    const mood = button.dataset.mood;
    if (!moodAssets[mood] || !expression) return;
    document.querySelectorAll('.mood').forEach(item => item.classList.remove('active'));
    button.classList.add('active');
    expression.classList.add('swapping');
    window.setTimeout(() => {
      expression.src = moodAssets[mood];
      expression.alt = `Baki-Pi — humor ${button.textContent.trim().toLowerCase()}`;
      expression.classList.remove('swapping');
    }, reduceMotion ? 0 : 180);
    if (line) line.textContent = `“${button.dataset.label}”`;
  });
});

if (!reduceMotion) {
  const glow = document.querySelector('.cursor-glow');
  const character = document.querySelector('.hero-character');
  let pointerX = window.innerWidth / 2;
  let pointerY = window.innerHeight / 2;
  let currentX = pointerX;
  let currentY = pointerY;

  window.addEventListener('pointermove', event => {
    pointerX = event.clientX;
    pointerY = event.clientY;
    if (character) {
      const x = (event.clientX / window.innerWidth - .5) * 18;
      const y = (event.clientY / window.innerHeight - .5) * 15;
      character.style.setProperty('--parallax-x', `${x}px`);
      character.style.setProperty('--parallax-y', `${y}px`);
      character.style.setProperty('--parallax-r', `${x * .06}deg`);
    }
  }, { passive: true });

  function animatePointer() {
    currentX += (pointerX - currentX) * .1;
    currentY += (pointerY - currentY) * .1;
    if (glow) {
      glow.style.left = `${currentX}px`;
      glow.style.top = `${currentY}px`;
    }
    requestAnimationFrame(animatePointer);
  }
  animatePointer();

  document.querySelectorAll('.magnetic').forEach(button => {
    button.addEventListener('pointermove', event => {
      const rect = button.getBoundingClientRect();
      const x = event.clientX - rect.left - rect.width / 2;
      const y = event.clientY - rect.top - rect.height / 2;
      button.style.transform = `translate(${x * .045}px, ${y * .08}px)`;
    });
    button.addEventListener('pointerleave', () => { button.style.transform = ''; });
  });
}

const canvas = document.querySelector('#starfield');
const context = canvas?.getContext('2d');
let stars = [];

function resizeStars() {
  if (!canvas || !context) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.floor(window.innerWidth * dpr);
  canvas.height = Math.floor(window.innerHeight * dpr);
  canvas.style.width = `${window.innerWidth}px`;
  canvas.style.height = `${window.innerHeight}px`;
  context.setTransform(dpr, 0, 0, dpr, 0, 0);
  const count = Math.min(150, Math.floor(window.innerWidth * window.innerHeight / 11000));
  stars = Array.from({ length: count }, () => ({
    x: Math.random() * window.innerWidth,
    y: Math.random() * window.innerHeight,
    radius: Math.random() * 1.45 + .25,
    alpha: Math.random() * .65 + .2,
    speed: Math.random() * .009 + .003,
    phase: Math.random() * Math.PI * 2
  }));
}

function drawStars(time = 0) {
  if (!canvas || !context) return;
  context.clearRect(0, 0, window.innerWidth, window.innerHeight);
  stars.forEach(star => {
    const alpha = reduceMotion ? star.alpha : star.alpha * (.65 + Math.sin(time * star.speed + star.phase) * .35);
    context.beginPath();
    context.arc(star.x, star.y, star.radius, 0, Math.PI * 2);
    context.fillStyle = `rgba(157, 222, 255, ${Math.max(.08, alpha)})`;
    context.fill();
  });
  if (!reduceMotion) requestAnimationFrame(drawStars);
}

resizeStars();
drawStars();
window.addEventListener('resize', resizeStars, { passive: true });

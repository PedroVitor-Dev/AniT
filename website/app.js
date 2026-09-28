const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
let reduceMotion = motionQuery.matches;
motionQuery.addEventListener?.('change', event => { reduceMotion = event.matches; });

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];

const releaseFallback = 'https://github.com/PedroVitor-Dev/AniT/releases/latest';
const canvas = $('#starfield');
const starContext = canvas?.getContext('2d', { alpha: true });
let stars = [];
let starAnimation;

async function resolveLatestRelease() {
  const links = $$('.release-link');
  const metadata = $$('.release-meta');
  const navVersion = $('.nav-version');
  try {
    const response = await fetch('https://api.github.com/repos/PedroVitor-Dev/AniT/releases/latest', {
      headers: { Accept: 'application/vnd.github+json' }
    });
    if (!response.ok) throw new Error('Release indisponível');
    const release = await response.json();
    const installer = release.assets?.find(asset => /AniT-Setup-.*-win-x64\.exe$/i.test(asset.name));
    const destination = installer?.browser_download_url || release.html_url || releaseFallback;
    const version = String(release.tag_name || '').replace(/^v/i, '') || 'mais recente';
    const size = installer?.size ? `${Math.round(installer.size / 1024 / 1024)} MB` : 'Windows x64';
    links.forEach(link => { link.href = destination; });
    metadata.forEach(item => { item.textContent = `Versão ${version} · ${size}`; });
    if (navVersion) navVersion.textContent = `v${version}`;
  } catch {
    links.forEach(link => { link.href = releaseFallback; });
    metadata.forEach(item => { item.textContent = 'Versão mais recente · Windows x64'; });
  }
}

resolveLatestRelease();

const guide = $('#bakiGuide');
const guideText = $('#guideText');
const guideBaki = $('#guideBaki');
let guideTimer;

function speak(message, mood = 'normal', duration = 5200) {
  if (!guide || !guideText || guide.classList.contains('dismissed')) return;
  const images = {
    normal: './assets/baki-normal.png',
    happy: './assets/baki-happy.png',
    focused: './assets/baki-angry.png',
    sleepy: './assets/baki-sleepy.png'
  };
  guideText.textContent = message;
  if (guideBaki) guideBaki.src = images[mood] || images.normal;
  guide.classList.add('visible');
  window.clearTimeout(guideTimer);
  guideTimer = window.setTimeout(() => guide.classList.remove('visible'), duration);
}

$('#closeGuide')?.addEventListener('click', () => {
  guide?.classList.add('dismissed');
  window.clearTimeout(guideTimer);
});

window.setTimeout(() => speak('Eu cuido das memórias. Você escolhe a próxima história.', 'normal'), 1100);

const atmosphereMessages = {
  madrugada: ['Madrugada acesa. Uma história calma combina com esse céu.', 'sleepy', '#030814'],
  nostalgia: ['Nostalgia escolhida. Até o que passou merece uma luz bonita.', 'normal', '#100b18'],
  maratona: ['Modo maratona. Água por perto e próximo episódio preparado.', 'focused', '#020712']
};

function readPreference(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

function savePreference(key, value) {
  try { localStorage.setItem(key, value); } catch { /* preferências são opcionais */ }
}

function applyAtmosphere(name, announce = false) {
  const safeName = atmosphereMessages[name] ? name : 'madrugada';
  document.documentElement.dataset.atmosphere = safeName;
  $$('.atmosphere-option').forEach(button => {
    const active = button.dataset.atmosphere === safeName;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  const metaTheme = $('meta[name="theme-color"]');
  if (metaTheme) metaTheme.content = atmosphereMessages[safeName][2];
  savePreference('anit-atmosphere', safeName);
  recolorStars();
  if (announce) speak(atmosphereMessages[safeName][0], atmosphereMessages[safeName][1]);
}

applyAtmosphere(readPreference('anit-atmosphere') || 'madrugada');

$$('.atmosphere-option').forEach(button => {
  button.addEventListener('click', () => applyAtmosphere(button.dataset.atmosphere, true));
});

$('.reset-atmosphere')?.addEventListener('click', () => applyAtmosphere('madrugada', true));

const topbar = $('.topbar');
function updateHeader() { topbar?.classList.toggle('scrolled', window.scrollY > 32); }
updateHeader();
window.addEventListener('scroll', updateHeader, { passive: true });

if ('IntersectionObserver' in window) {
  const revealObserver = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add('visible');
        revealObserver.unobserve(entry.target);
      }
    });
  }, { threshold: .12, rootMargin: '0px 0px -5% 0px' });
  $$('.reveal:not(.visible)').forEach(element => revealObserver.observe(element));
} else {
  $$('.reveal').forEach(element => element.classList.add('visible'));
}

if (!reduceMotion) {
  const heroArchive = $('.hero-archive');
  window.addEventListener('pointermove', event => {
    document.documentElement.style.setProperty('--pointer-x', `${event.clientX}px`);
    document.documentElement.style.setProperty('--pointer-y', `${event.clientY}px`);
    if (!heroArchive || window.innerWidth < 800) return;
    const x = (event.clientX / window.innerWidth - .5) * 14;
    const y = (event.clientY / window.innerHeight - .5) * 9;
    heroArchive.style.setProperty('--archive-x', `${x}px`);
    heroArchive.style.setProperty('--archive-y', `${y}px`);
  }, { passive: true });
}

const rawFiles = $$('.raw-file');
const organizerCore = $('#organizerCore');
const organizedShelf = $('#organizedShelf');
const coreLabel = $('#coreLabel');
const coreHint = $('#coreHint');
const organizerStatus = $('#organizerStatus');
const recognizedFiles = new Set();
let organizerBusy = false;

function updateOrganizerProgress() {
  const count = recognizedFiles.size;
  if (coreHint) coreHint.textContent = `${count} de ${rawFiles.length} reconhecidos`;
  if (coreLabel) coreLabel.textContent = count ? 'Padrões encontrados' : 'Solte ou ative aqui';
  rawFiles.forEach(file => file.classList.toggle('selected', recognizedFiles.has(file.dataset.file)));
  if (count === rawFiles.length) finishOrganizer();
}

function recognizeFile(file) {
  if (!file || organizerBusy || recognizedFiles.has(file.dataset.file)) return;
  recognizedFiles.add(file.dataset.file);
  updateOrganizerProgress();
  if (organizerStatus) organizerStatus.textContent = `${recognizedFiles.size} arquivo(s) reconhecido(s). O nome muda; a ordem permanece.`;
  if (recognizedFiles.size < rawFiles.length) speak('Reconheci o padrão. Ainda há memórias para colocar no lugar.', 'focused', 3300);
}

function finishOrganizer() {
  if (organizerBusy || organizedShelf?.classList.contains('ready')) return;
  organizerBusy = true;
  if (coreLabel) coreLabel.textContent = 'Organizando…';
  if (organizerStatus) organizerStatus.textContent = 'Separando temporada e episódios…';
  const delay = reduceMotion ? 0 : 520;
  window.setTimeout(() => {
    rawFiles.forEach(file => file.classList.add('processed'));
    organizedShelf?.classList.add('ready');
    if (coreLabel) coreLabel.textContent = 'Tudo em ordem';
    if (coreHint) coreHint.textContent = '3 episódios · temporada 01';
    if (organizerStatus) organizerStatus.textContent = 'Pronto: três nomes diferentes viraram uma única temporada organizada.';
    organizerBusy = false;
    speak('Tudo em ordem! Três arquivos diferentes, uma só história.', 'happy', 6000);
  }, delay);
}

function organizeAll() {
  if (organizerBusy) return;
  rawFiles.forEach(file => recognizedFiles.add(file.dataset.file));
  updateOrganizerProgress();
}

function resetOrganizer() {
  organizerBusy = false;
  recognizedFiles.clear();
  rawFiles.forEach(file => file.classList.remove('selected', 'processed'));
  organizedShelf?.classList.remove('ready');
  if (coreLabel) coreLabel.textContent = 'Solte ou ative aqui';
  if (coreHint) coreHint.textContent = `0 de ${rawFiles.length} reconhecidos`;
  if (organizerStatus) organizerStatus.textContent = 'Escolha um arquivo ou organize tudo de uma vez.';
  speak('A demonstração voltou ao começo. Pode bagunçar de novo.', 'normal', 3600);
}

rawFiles.forEach(file => {
  file.addEventListener('click', () => recognizeFile(file));
  file.addEventListener('dragstart', event => {
    event.dataTransfer?.setData('text/plain', file.dataset.file);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  });
});

organizerCore?.addEventListener('dragover', event => { event.preventDefault(); organizerCore.classList.add('drag-over'); });
organizerCore?.addEventListener('dragleave', () => organizerCore.classList.remove('drag-over'));
organizerCore?.addEventListener('drop', event => {
  event.preventDefault();
  organizerCore.classList.remove('drag-over');
  const id = event.dataTransfer?.getData('text/plain');
  recognizeFile(rawFiles.find(file => file.dataset.file === id));
});
organizerCore?.addEventListener('click', organizeAll);
organizerCore?.addEventListener('keydown', event => {
  if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); organizeAll(); }
});
$('#organizeAll')?.addEventListener('click', organizeAll);
$('#resetOrganizer')?.addEventListener('click', resetOrganizer);

const journeyStates = {
  quero: { progress: 0, time: 'Ainda não iniciado', percent: '0%', action: 'Começar o episódio', message: 'Adicionado à jornada. Quando começar, eu guardo o caminho.', mood: 'normal' },
  assistindo: { progress: 77, time: '18:42 de 24:10', percent: '77%', action: 'Continuar do ponto salvo', message: 'Seu ponto continua aqui. Nem um segundo da história se perde.', mood: 'happy' },
  concluido: { progress: 100, time: '24:10 de 24:10', percent: '100%', action: 'Assistir novamente', message: 'Concluído! Mais uma memória acesa na sua jornada.', mood: 'happy' }
};
let currentJourneyState = 'assistindo';

function setJourneyState(name, announce = true) {
  const state = journeyStates[name] || journeyStates.assistindo;
  currentJourneyState = name;
  $$('.state-switcher button').forEach(button => {
    const active = button.dataset.state === name;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  const progress = $('#journeyProgress');
  const time = $('#journeyTime');
  const percent = $('#journeyPercent');
  const action = $('#resumeJourney');
  if (progress) progress.style.width = `${state.progress}%`;
  if (time) time.textContent = state.time;
  if (percent) percent.textContent = state.percent;
  if (action) action.innerHTML = `<span aria-hidden="true">▶</span> ${state.action}`;
  const stamp = $('#memoryStamp');
  stamp?.classList.remove('flash');
  requestAnimationFrame(() => stamp?.classList.add('flash'));
  if (announce) speak(state.message, state.mood);
}

$$('.state-switcher button').forEach(button => button.addEventListener('click', () => setJourneyState(button.dataset.state)));
$('#resumeJourney')?.addEventListener('click', () => {
  if (currentJourneyState === 'concluido') return setJourneyState('assistindo');
  if (currentJourneyState === 'quero') return setJourneyState('assistindo');
  const progress = $('#journeyProgress');
  const percent = $('#journeyPercent');
  const time = $('#journeyTime');
  if (progress) progress.style.width = '84%';
  if (percent) percent.textContent = '84%';
  if (time) time.textContent = '20:19 de 24:10';
  speak('Retomado. O céu estava esperando exatamente neste ponto.', 'happy');
});

const moodAssets = {
  normal: './assets/baki-normal.png',
  happy: './assets/baki-happy.png',
  focused: './assets/baki-angry.png',
  sleepy: './assets/baki-sleepy.png'
};
const expression = $('#bakiExpression');
const bakiLine = $('#bakiLine');

$$('.mood').forEach(button => {
  button.addEventListener('click', () => {
    const mood = button.dataset.mood;
    if (!moodAssets[mood] || !expression) return;
    $$('.mood').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
    expression.classList.add('swapping');
    window.setTimeout(() => {
      expression.src = moodAssets[mood];
      expression.alt = `Baki-Pi — humor ${button.textContent.trim().toLowerCase()}`;
      expression.classList.remove('swapping');
    }, reduceMotion ? 0 : 170);
    const message = button.dataset.label || '';
    if (bakiLine) bakiLine.textContent = `“${message}”`;
    speak(message, mood, 4000);
  });
});

const oracleData = {
  aventura: { title: 'AVENTURA', phrase: 'Hoje a sua curiosidade aponta para um mundo que ainda não tem mapa.', colors: ['#04142f', '#087dbb', '#7069ff'], mood: 'happy' },
  conforto: { title: 'CONFORTO', phrase: 'Uma história familiar também pode abrir uma janela nova por dentro.', colors: ['#160d24', '#935f91', '#ffb878'], mood: 'normal' },
  misterio: { title: 'MISTÉRIO', phrase: 'Há uma pista escondida no episódio que você quase deixou para amanhã.', colors: ['#020712', '#153b6b', '#9d5cff'], mood: 'focused' }
};
let selectedOracle = 'aventura';
let lastShareText = '';

$$('.oracle-option').forEach(button => {
  button.addEventListener('click', () => {
    selectedOracle = button.dataset.oracle;
    $$('.oracle-option').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
  });
});

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = reject;
    image.src = src;
  });
}

function wrapCanvasText(context, text, x, y, maxWidth, lineHeight) {
  const words = text.split(' ');
  let line = '';
  let row = 0;
  words.forEach(word => {
    const test = `${line}${word} `;
    if (context.measureText(test).width > maxWidth && line) {
      context.fillText(line.trim(), x, y + row * lineHeight);
      line = `${word} `;
      row += 1;
    } else {
      line = test;
    }
  });
  context.fillText(line.trim(), x, y + row * lineHeight);
  return row + 1;
}

function seededRandom(seed) {
  let value = seed % 2147483647;
  return () => { value = value * 16807 % 2147483647; return (value - 1) / 2147483646; };
}

async function generateJourneyCard() {
  const canvas = $('#journeyCard');
  const placeholder = $('#cardPlaceholder');
  const shareActions = $('#shareActions');
  const download = $('#downloadJourneyCard');
  const status = $('#shareStatus');
  const data = oracleData[selectedOracle];
  if (!canvas || !data) return;
  const button = $('#generateJourneyCard');
  if (button) { button.disabled = true; button.textContent = 'Acendendo…'; }
  try {
    await document.fonts?.ready;
    const [baki, logo] = await Promise.all([loadImage(moodAssets[data.mood]), loadImage('./assets/anit-logo.png')]);
    const context = canvas.getContext('2d');
    const gradient = context.createLinearGradient(0, 0, 1080, 1080);
    gradient.addColorStop(0, data.colors[0]);
    gradient.addColorStop(.55, data.colors[1]);
    gradient.addColorStop(1, data.colors[2]);
    context.fillStyle = gradient;
    context.fillRect(0, 0, 1080, 1080);

    const glow = context.createRadialGradient(730, 360, 20, 730, 360, 520);
    glow.addColorStop(0, 'rgba(99,231,255,.36)');
    glow.addColorStop(1, 'rgba(4,9,23,0)');
    context.fillStyle = glow;
    context.fillRect(0, 0, 1080, 1080);

    const random = seededRandom(selectedOracle.length * 7919);
    for (let i = 0; i < 92; i += 1) {
      const x = random() * 1080;
      const y = random() * 1080;
      const r = random() * 2.2 + .4;
      context.beginPath();
      context.arc(x, y, r, 0, Math.PI * 2);
      context.fillStyle = `rgba(210,246,255,${.18 + random() * .62})`;
      context.fill();
    }

    context.strokeStyle = 'rgba(126,221,255,.22)';
    context.lineWidth = 2;
    [220, 310, 405].forEach(radius => {
      context.beginPath();
      context.arc(775, 565, radius, Math.PI * .18, Math.PI * 1.7);
      context.stroke();
    });

    context.drawImage(logo, 68, 66, 210, 118);
    context.drawImage(baki, 565, 280, 490, 490);
    context.fillStyle = 'rgba(1,7,21,.68)';
    context.fillRect(0, 750, 1080, 330);
    context.fillStyle = '#66e6ff';
    context.font = '800 26px Inter, sans-serif';
    context.letterSpacing = '4px';
    context.fillText('SINAL DA PRÓXIMA HISTÓRIA', 68, 820);
    context.fillStyle = '#ffffff';
    context.font = '900 76px Inter, sans-serif';
    context.letterSpacing = '-2px';
    context.fillText(data.title, 68, 910);
    context.font = '600 28px Inter, sans-serif';
    context.fillStyle = 'rgba(236,248,255,.86)';
    wrapCanvasText(context, data.phrase, 68, 966, 880, 38);
    context.font = '700 20px Inter, sans-serif';
    context.fillStyle = 'rgba(174,210,232,.7)';
    context.fillText('Criado com Baki-Pi · AniT', 68, 1040);

    canvas.classList.add('ready');
    if (placeholder) placeholder.hidden = true;
    if (shareActions) shareActions.hidden = false;
    const dataUrl = canvas.toDataURL('image/png');
    if (download) download.href = dataUrl;
    lastShareText = `${data.title}: ${data.phrase} — Baki-Pi no AniT`;
    if (status) status.textContent = 'Cartão pronto. Baixe a imagem ou compartilhe a frase.';
    speak('Seu sinal está aceso. Essa memória já pode viajar com você.', data.mood, 5400);
  } catch {
    if (status) status.textContent = 'Não foi possível gerar o cartão neste navegador.';
  } finally {
    if (button) { button.disabled = false; button.textContent = 'Acender meu cartão'; }
  }
}

$('#generateJourneyCard')?.addEventListener('click', generateJourneyCard);
$('#shareJourneyCard')?.addEventListener('click', async () => {
  if (!lastShareText) return;
  const status = $('#shareStatus');
  try {
    if (navigator.share) {
      await navigator.share({ title: 'Minha próxima história · AniT', text: lastShareText, url: 'https://pedrovitor-dev.github.io/AniT/' });
      if (status) status.textContent = 'Frase compartilhada.';
    } else {
      await navigator.clipboard.writeText(`${lastShareText}\nhttps://pedrovitor-dev.github.io/AniT/`);
      if (status) status.textContent = 'Frase copiada para compartilhar.';
    }
  } catch (error) {
    if (error?.name !== 'AbortError' && status) status.textContent = 'Compartilhamento cancelado. Você ainda pode baixar o cartão.';
  }
});

const demoPlayer = $('#demoPlayer');
const demoNumber = $('#demoNumber');
const demoName = $('#demoName');
const demoDescription = $('#demoDescription');

$$('.demo-choice').forEach(button => {
  button.addEventListener('click', () => {
    const nextVideo = button.dataset.video;
    if (!demoPlayer || !nextVideo) return;
    $$('.demo-choice').forEach(choice => {
      const active = choice === button;
      choice.classList.toggle('active', active);
      choice.setAttribute('aria-pressed', String(active));
    });
    if (demoNumber) demoNumber.textContent = button.dataset.number || '';
    if (demoName) demoName.textContent = button.dataset.title || '';
    if (demoDescription) demoDescription.textContent = button.dataset.description || '';
    demoPlayer.setAttribute('aria-label', `Demonstração: ${button.dataset.title || 'AniT'}`);
    demoPlayer.pause();
    demoPlayer.src = nextVideo;
    demoPlayer.load();
    demoPlayer.play().catch(() => {});
    speak(`Agora: ${button.dataset.title}. É o aplicativo real em movimento.`, 'focused', 3300);
  });
});

if (demoPlayer && 'IntersectionObserver' in window) {
  const videoObserver = new IntersectionObserver(entries => {
    if (!entries[0].isIntersecting) demoPlayer.pause();
  }, { threshold: .18 });
  videoObserver.observe(demoPlayer);
}

function starPalette() {
  const atmosphere = document.documentElement.dataset.atmosphere;
  if (atmosphere === 'nostalgia') return ['255,211,177', '222,151,220'];
  if (atmosphere === 'maratona') return ['105,249,255', '98,153,255'];
  return ['157,222,255', '151,146,255'];
}

function resizeStars() {
  if (!canvas || !starContext) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.floor(window.innerWidth * dpr);
  canvas.height = Math.floor(window.innerHeight * dpr);
  canvas.style.width = `${window.innerWidth}px`;
  canvas.style.height = `${window.innerHeight}px`;
  starContext.setTransform(dpr, 0, 0, dpr, 0, 0);
  const count = Math.min(120, Math.floor(window.innerWidth * window.innerHeight / 13500));
  stars = Array.from({ length: count }, (_, index) => ({
    x: Math.random() * window.innerWidth,
    y: Math.random() * window.innerHeight,
    radius: Math.random() * 1.35 + .3,
    alpha: Math.random() * .58 + .18,
    speed: Math.random() * .006 + .002,
    phase: Math.random() * Math.PI * 2,
    color: index % 5 === 0 ? 1 : 0
  }));
}

function recolorStars() { if (starContext) drawStars(performance.now(), true); }

function drawStars(time = 0, once = false) {
  if (!canvas || !starContext || document.hidden) return;
  const colors = starPalette();
  starContext.clearRect(0, 0, window.innerWidth, window.innerHeight);
  stars.forEach(star => {
    const pulse = reduceMotion ? 1 : .7 + Math.sin(time * star.speed + star.phase) * .3;
    starContext.beginPath();
    starContext.arc(star.x, star.y, star.radius, 0, Math.PI * 2);
    starContext.fillStyle = `rgba(${colors[star.color]},${Math.max(.07, star.alpha * pulse)})`;
    starContext.fill();
  });
  if (!once && !reduceMotion) starAnimation = requestAnimationFrame(drawStars);
}

resizeStars();
drawStars();
window.addEventListener('resize', resizeStars, { passive: true });

document.addEventListener('visibilitychange', () => {
  document.body.dataset.pageHidden = String(document.hidden);
  if (document.hidden) {
    cancelAnimationFrame(starAnimation);
    demoPlayer?.pause();
  } else if (!reduceMotion) {
    cancelAnimationFrame(starAnimation);
    starAnimation = requestAnimationFrame(drawStars);
  }
});

// The HTML deck's one script: stepping through builds and slides with their effects and transitions, the notes view,
// the presenter window, the overview, the black screen and the key list.
// Plain JavaScript, not minified, so it can be reviewed like code.
(function () {
  'use strict';

  var slides = Array.prototype.slice.call(document.querySelectorAll('section.slide'));
  var body = document.body;
  var deck = document.getElementById('deck');
  var counter = document.getElementById('counter');
  var help = document.getElementById('help');
  var presenterMode = window.location.hash === '#presenter';
  var reducedMotion = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
  var current = 0;
  var step = 0;
  var selected = 0;
  var typed = '';

  // Each slide's build steps and transition, as the page writes them on its section.
  var info = slides.map(function (slide) {
    var steps = [];
    try {
      steps = JSON.parse(slide.getAttribute('data-steps') || '[]');
    } catch (e) {
      steps = [];
    }

    return {
      steps: Array.isArray(steps) ? steps : [],
      transition: slide.getAttribute('data-transition'),
      direction: slide.getAttribute('data-direction'),
      duration: parseInt(slide.getAttribute('data-duration') || '0', 10) || 0,
      line: parseInt(slide.getAttribute('data-line') || '0', 10) || 0
    };
  });

  function animated() {
    return !presenterMode && !(reducedMotion && reducedMotion.matches);
  }

  // ---- Builds ---------------------------------------------------------------------------------------------------

  // The element an effect works on, inside a slide's SVG (or a copy of it): the slide's own item with that id (not a
  // decoration's, not an inner part with the same id), or that item's paragraphs.
  function targets(root, effect) {
    var own = root.querySelector('g.own');
    if (!own || typeof effect.id !== 'number') {
      return [];
    }

    var selector = '[data-id="' + effect.id + '"]';
    var item = null;
    var all = own.querySelectorAll(selector);
    for (var i = 0; i < all.length && !item; i++) {
      var outer = all[i].parentElement ? all[i].parentElement.closest(selector) : null;
      if (!outer || !own.contains(outer)) {
        item = all[i];
      }
    }

    if (!item) {
      return [];
    }

    if (!effect.p) {
      return [item];
    }

    return Array.prototype.slice.call(item.querySelectorAll('[data-p]')).filter(function (p) {
      var index = parseInt(p.getAttribute('data-p'), 10);
      return index >= effect.p[0] && index <= effect.p[1];
    });
  }

  // Shows a slide after <count> of its steps, at once: what enters later is hidden, what has left is hidden. What is
  // hidden after the last step is also marked as such, so print shows the final state whatever step is current.
  function render(root, steps, count) {
    Array.prototype.forEach.call(root.querySelectorAll('.build-hidden, .build-gone'), function (element) {
      element.classList.remove('build-hidden', 'build-gone');
    });
    var state = new Map();
    var final = new Map();
    steps.forEach(function (effects, index) {
      effects.forEach(function (effect) {
        targets(root, effect).forEach(function (element) {
          if (!state.has(element)) {
            state.set(element, effect.c !== 'in');
            final.set(element, effect.c !== 'in');
          }

          if (effect.c !== 'em') {
            final.set(element, effect.c === 'in');
            if (index < count) {
              state.set(element, effect.c === 'in');
            }
          }
        });
      });
    });
    state.forEach(function (visible, element) {
      if (!visible) {
        element.classList.add('build-hidden');
      }

      if (!final.get(element)) {
        element.classList.add('build-gone');
      }
    });
  }

  var running = [];

  function settle() {
    running.slice().forEach(finish);
  }

  function finish(entry) {
    var index = running.indexOf(entry);
    if (index < 0) {
      return;
    }

    running.splice(index, 1);
    if (entry.done) {
      entry.done();
    }

    entry.animation.cancel();
  }

  function animate(element, keyframes, duration, done) {
    if (!animated() || !element.animate || !duration) {
      if (done) {
        done();
      }

      return;
    }

    var entry = { animation: element.animate(keyframes, { duration: duration, easing: 'ease-in-out', fill: 'forwards' }), done: done };
    running.push(entry);
    entry.animation.onfinish = function () {
      finish(entry);
    };
  }

  // An element's box in its SVG's user space (points of the slide).
  function box(element) {
    var svg = element.ownerSVGElement || element;
    var matrix = svg.getScreenCTM();
    var rect = element.getBoundingClientRect();
    if (!matrix) {
      return null;
    }

    var inverse = matrix.inverse();
    var point = svg.createSVGPoint();
    point.x = rect.left;
    point.y = rect.top;
    var topLeft = point.matrixTransform(inverse);
    point.x = rect.right;
    point.y = rect.bottom;
    var bottomRight = point.matrixTransform(inverse);
    var view = svg.viewBox.baseVal;
    return { left: topLeft.x, top: topLeft.y, right: bottomRight.x, bottom: bottomRight.y, width: view.width, height: view.height };
  }

  // The offset that puts an element just outside the slide on the given sides.
  function offside(element, side) {
    var b = box(element);
    if (!b) {
      return '0px 0px';
    }

    var dx = side.indexOf('l') >= 0 ? -b.right : side.indexOf('r') >= 0 ? b.width - b.left : 0;
    var dy = side.indexOf('t') >= 0 ? -b.bottom : side.indexOf('b') >= 0 ? b.height - b.top : 0;
    return dx + 'px ' + dy + 'px';
  }

  // The clip that hides an element before a wipe from that side.
  function wiped(side) {
    switch (side) {
      case 'l':
        return 'inset(0 100% 0 0)';
      case 'r':
        return 'inset(0 0 0 100%)';
      case 't':
        return 'inset(0 0 100% 0)';
      default:
        return 'inset(100% 0 0 0)';
    }
  }

  // Plays one step's effects, from the state before it: entrances show and play in, exits play out and hide,
  // emphasis pulses.
  function play(root, effects) {
    effects.forEach(function (effect) {
      targets(root, effect).forEach(function (element) {
        var side = effect.d || 'b';
        var duration = effect.t || 0;
        var frames = null;
        switch (effect.e) {
          case 'fade':
            frames = [{ opacity: 0 }, { opacity: 1 }];
            break;
          case 'fly':
            frames = [{ translate: offside(element, side) }, { translate: '0px 0px' }];
            break;
          case 'wipe':
            frames = [{ clipPath: wiped(side) }, { clipPath: 'inset(0 0 0 0)' }];
            break;
          case 'pulse':
            var b = box(element);
            if (b) {
              var cx = (b.left + b.right) / 2;
              var cy = (b.top + b.bottom) / 2;
              var s = 1.05;
              frames = [
                { translate: '0px 0px', scale: '1' },
                { translate: (cx * (1 - s)) + 'px ' + (cy * (1 - s)) + 'px', scale: String(s) },
                { translate: '0px 0px', scale: '1' }
              ];
            }

            break;
        }

        if (effect.c === 'in') {
          element.classList.remove('build-hidden');
          if (frames) {
            animate(element, frames, duration);
          }
        } else if (effect.c === 'out') {
          var hide = function () {
            element.classList.add('build-hidden');
          };
          if (frames) {
            animate(element, frames.slice().reverse(), duration, hide);
          } else {
            hide();
          }
        } else if (frames && effect.e === 'pulse') {
          animate(element, frames, duration);
        }
      });
    });
  }

  // ---- Transitions ----------------------------------------------------------------------------------------------

  var moves = {
    l: ['100%', '0%'], r: ['-100%', '0%'], u: ['0%', '100%'], d: ['0%', '-100%'],
    lu: ['100%', '100%'], ru: ['-100%', '100%'], ld: ['100%', '-100%'], rd: ['-100%', '-100%']
  };

  function negate(value) {
    return value.charAt(0) === '-' ? value.substring(1) : value === '0%' ? value : '-' + value;
  }

  // Plays the transition into <incoming> over <outgoing>; the outgoing slide stays shown until it ends.
  function transition(outgoing, incoming, data, after) {
    var kind = data.transition;
    var duration = data.duration;
    if (!kind || kind === 'cut' || !animated() || !duration || !incoming.animate) {
      after();
      return;
    }

    var move = moves[data.direction] || moves.l;
    var from = 'translate(' + move[0] + ', ' + move[1] + ')';
    var away = 'translate(' + negate(move[0]) + ', ' + negate(move[1]) + ')';
    var leave = function () {
      outgoing.classList.remove('leaving', 'on-top');
      after();
    };
    outgoing.classList.add('leaving');
    var split = (data.direction || 'horz-out').split('-');
    var closed = split[0] === 'vert' ? 'inset(0 50% 0 50%)' : 'inset(50% 0 50% 0)';
    switch (kind) {
      case 'push':
        animate(incoming, [{ transform: from }, { transform: 'none' }], duration);
        animate(outgoing, [{ transform: 'none' }, { transform: away }], duration, leave);
        return;
      case 'wipe':
        animate(incoming, [{ clipPath: wipeIn(data.direction) }, { clipPath: 'inset(0 0 0 0)' }], duration, leave);
        return;
      case 'cover':
        animate(incoming, [{ transform: from }, { transform: 'none' }], duration, leave);
        return;
      case 'uncover':
        outgoing.classList.add('on-top');
        animate(outgoing, [{ transform: 'none' }, { transform: away }], duration, leave);
        return;
      case 'split':
        if (split[1] === 'in') {
          outgoing.classList.add('on-top');
          animate(outgoing, [{ clipPath: 'inset(0 0 0 0)' }, { clipPath: closed }], duration, leave);
        } else {
          animate(incoming, [{ clipPath: closed }, { clipPath: 'inset(0 0 0 0)' }], duration, leave);
        }

        return;
      case 'zoom':
        if (data.direction === 'out') {
          outgoing.classList.add('on-top');
          animate(outgoing, [{ transform: 'none', opacity: 1 }, { transform: 'scale(1.5)', opacity: 0 }], duration, leave);
        } else {
          animate(incoming, [{ transform: 'scale(0.3)', opacity: 0 }, { transform: 'none', opacity: 1 }], duration, leave);
        }

        return;
      case 'morph':
        morph(outgoing, incoming, duration, leave);
        return;
      default:
        // Fade and anything else.
        animate(incoming, [{ opacity: 0 }, { opacity: 1 }], duration, leave);
    }
  }

  // The shown top-level items of a slide's own group, each with its Morph name (!!name) if it has one.
  function morphItems(slide) {
    var own = slide.querySelector('g.own');
    if (!own) {
      return [];
    }

    return Array.prototype.slice.call(own.children).filter(function (child) {
      return !child.classList.contains('build-hidden') && !child.querySelector(':scope > .build-hidden');
    }).map(function (child) {
      var name = child.getAttribute('data-name');
      if (!name && child.firstElementChild) {
        name = child.firstElementChild.getAttribute('data-name');
      }

      return { element: child, name: name || null };
    });
  }

  // The individual transform that lays box <from> over box <to>, in slide points. Along an axis where either box is
  // thin (a line across or up, with its arrow head) or the size changes more than fivefold, it only moves, centre to centre.
  function lay(from, to) {
    function axis(fromStart, fromEnd, toStart, toEnd) {
      var s = (toEnd - toStart) / (fromEnd - fromStart);
      if (fromEnd - fromStart >= 10 && toEnd - toStart >= 10 && s >= 0.2 && s <= 5) {
        return { scale: s, move: toStart - (s * fromStart) };
      }

      return { scale: 1, move: ((toStart + toEnd) / 2) - ((fromStart + fromEnd) / 2) };
    }

    var x = axis(from.left, from.right, to.left, to.right);
    var y = axis(from.top, from.bottom, to.top, to.bottom);
    return { translate: x.move + 'px ' + y.move + 'px', scale: x.scale + ' ' + y.scale };
  }

  // Morph: items of the same name on both slides move and resize from where they were to where they are, the old one
  // fading out over the new one; the others fade out or in. The leaving slide stays on top with its own items only.
  function morph(outgoing, incoming, duration, leave) {
    var before = morphItems(outgoing);
    var after = morphItems(incoming);
    var byName = new Map();
    before.forEach(function (entry) {
      if (entry.name && !byName.has(entry.name)) {
        byName.set(entry.name, entry);
      }
    });
    var moved = new Set();
    var still = { translate: '0px 0px', scale: '1 1' };
    outgoing.classList.add('on-top', 'morphing');
    after.forEach(function (entry) {
      var match = entry.name ? byName.get(entry.name) : null;
      var from = match && !moved.has(match) ? box(match.element) : null;
      var to = from ? box(entry.element) : null;
      if (from && to) {
        moved.add(match);
        animate(entry.element, [lay(to, from), still], duration);
        var away = lay(from, to);
        animate(match.element, [{ translate: still.translate, scale: still.scale, opacity: 1 }, { translate: away.translate, scale: away.scale, opacity: 0 }], duration);
      } else {
        animate(entry.element, [{ opacity: 0 }, { opacity: 1 }], duration);
      }
    });
    before.forEach(function (entry) {
      if (!moved.has(entry)) {
        animate(entry.element, [{ opacity: 1 }, { opacity: 0 }], duration);
      }
    });

    // The slide itself carries the end of the transition.
    animate(outgoing, [{ opacity: 1 }, { opacity: 1 }], duration, function () {
      outgoing.classList.remove('morphing');
      leave();
    });
  }

  // The clip a wipe in the direction of <direction> starts from: "l" moves leftwards, so it starts at the right.
  function wipeIn(direction) {
    switch (direction) {
      case 'r':
        return 'inset(0 100% 0 0)';
      case 'u':
        return 'inset(100% 0 0 0)';
      case 'd':
        return 'inset(0 0 100% 0)';
      default:
        return 'inset(0 0 0 100%)';
    }
  }

  // ---- Moving -----------------------------------------------------------------------------------------------------

  function clampSlide(index) {
    return Math.min(Math.max(index, 0), slides.length - 1);
  }

  function fromHash() {
    var match = /^#(\d+)(?:\.(\d+))?$/.exec(window.location.hash);
    var index = clampSlide((match ? parseInt(match[1], 10) : 1) - 1);
    var count = match && match[2] ? parseInt(match[2], 10) : 0;
    return { slide: index, step: Math.min(count, info[index] ? info[index].steps.length : 0) };
  }

  // Goes to slide <index> after <count> steps. With <how> 'step' the one step before plays; with 'slide' the
  // transition plays; otherwise everything changes at once.
  function go(index, count, how, quiet) {
    if (slides.length === 0) {
      return;
    }

    settle();
    index = clampSlide(index);
    count = Math.min(Math.max(count, 0), info[index].steps.length);
    var previous = current;
    var root = slides[index];
    var inOverview = body.classList.contains('overview');
    if (how === 'step' && index === previous && count === step + 1 && !inOverview) {
      render(root, info[index].steps, count - 1);
      play(root, info[index].steps[count - 1]);
    } else {
      render(root, info[index].steps, inOverview ? info[index].steps.length : count);
    }

    current = index;
    step = count;
    slides.forEach(function (slide, i) {
      slide.classList.toggle('current', i === current);
      slide.setAttribute('aria-hidden', i === current || inOverview ? 'false' : 'true');
    });
    if (previous !== index) {
      var outgoing = slides[previous];

      // Every slide but the current one waits in its final state, for the overview and print.
      var final = function () {
        if (outgoing !== slides[current]) {
          render(outgoing, info[previous].steps, info[previous].steps.length);
        }
      };
      if (how === 'slide' && !inOverview) {
        transition(outgoing, root, info[index], final);
      } else {
        final();
      }
    }

    var total = info[current].steps.length;
    counter.textContent = (current + 1) + ' / ' + slides.length + (total > 0 ? ' · ' + step + '/' + total : '');
    if (!presenterMode) {
      var hash = '#' + (current + 1) + (step > 0 ? '.' + step : '');
      if (window.location.hash !== hash) {
        try {
          window.history.replaceState(null, '', hash);
        } catch (e) {
          // A page opened from disk in some browsers refuses history changes; the slide still shows.
        }
      }
    }

    updatePresenter();
    if (!quiet) {
      send({ kind: 'state', slide: current, step: step });
    }

    // An editor showing the deck beside its Markdown follows the slide back to its line.
    if (previous !== index && !presenterMode) {
      send({ kind: 'shown', slide: current + 1, line: info[current].line });
    }
  }

  function next() {
    if (step < info[current].steps.length) {
      go(current, step + 1, 'step');
    } else if (current < slides.length - 1) {
      go(current + 1, 0, 'slide');
    }
  }

  function previous() {
    if (step > 0) {
      go(current, step - 1);
    } else if (current > 0) {
      go(current - 1, info[current - 1].steps.length);
    }
  }

  // ---- Overview, black screen, key list, notes view ---------------------------------------------------------------

  function select(index) {
    selected = clampSlide(index);
    slides.forEach(function (slide, i) {
      slide.classList.toggle('selected', i === selected);
    });
    slides[selected].scrollIntoView({ block: 'nearest' });
  }

  function overview(on) {
    body.classList.toggle('overview', on);
    if (on) {
      select(current);
    }

    go(current, step, null, true);
  }

  function toggleBlack(on) {
    body.classList.toggle('black', on);
  }

  function toggleHelp(on) {
    help.hidden = !on;
  }

  function toggleNotes(on) {
    body.classList.toggle('notes-view', on);
  }

  function fullScreen() {
    var element = document.documentElement;
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else if (element.requestFullscreen) {
      element.requestFullscreen();
    }
  }

  // A slide without notes says so in the notes view; so does a page written without notes.
  var noNotes = deck.getAttribute('data-notes') === 'off' ? 'The speaker notes are not in this file.' : 'No speaker notes for this slide.';
  slides.forEach(function (slide) {
    if (!slide.querySelector('aside.notes')) {
      var aside = document.createElement('aside');
      aside.className = 'notes empty';
      aside.setAttribute('aria-label', 'Speaker notes');
      var p = document.createElement('p');
      p.textContent = noNotes;
      aside.appendChild(p);
      slide.appendChild(aside);
    }
  });

  // ---- Presenter window and sync ----------------------------------------------------------------------------------

  var peer = presenterMode ? window.opener : null;
  var channel = null;

  // The page around a deck shown in an iframe, such as an editor's preview: it hears what the deck sends, and its
  // messages never make it the presenter window.
  var embedder = null;
  try {
    embedder = window.parent && window.parent !== window ? window.parent : null;
  } catch (e) {
    embedder = null;
  }

  try {
    if (window.BroadcastChannel) {
      channel = new window.BroadcastChannel('html-deck:' + window.location.pathname);
      channel.onmessage = function (event) {
        receive(event.data, null);
      };
    }
  } catch (e) {
    channel = null;
  }

  function send(message) {
    message.app = 'html-deck';
    if (peer && !peer.closed) {
      try {
        // A page opened from disk has an opaque origin, so the target cannot be named.
        peer.postMessage(message, '*');
      } catch (e) {
        peer = null;
      }
    }

    if (embedder && embedder !== peer) {
      try {
        embedder.postMessage(message, '*');
      } catch (e) {
        embedder = null;
      }
    }

    if (channel) {
      try {
        channel.postMessage(message);
      } catch (e) {
        channel = null;
      }
    }
  }

  function receive(data, source) {
    if (!data || data.app !== 'html-deck') {
      return;
    }

    if (source && source !== embedder && (!peer || peer.closed)) {
      peer = source;
    }

    if (data.kind === 'hello') {
      send({ kind: 'state', slide: current, step: step });
    } else if (data.kind === 'state' && typeof data.slide === 'number' && typeof data.step === 'number') {
      if (data.slide === current && data.step === step) {
        return;
      }

      var how = data.slide === current && data.step === step + 1 ? 'step' : data.slide === current + 1 && data.step === 0 ? 'slide' : null;
      go(data.slide, data.step, how, true);
    } else if (data.kind === 'go') {
      // Slide n (from 1) at once, every build shown; the answer is the new state, which the presenter window follows.
      if (typeof data.slide !== 'number' || data.slide % 1 !== 0 || data.slide < 1 || data.slide > slides.length) {
        return;
      }

      go(data.slide - 1, info[data.slide - 1].steps.length);
    }
  }

  window.addEventListener('message', function (event) {
    if (event.source && event.source !== window) {
      receive(event.data, event.source);
    }
  });

  function openPresenter() {
    if (presenterMode) {
      return;
    }

    if (peer && !peer.closed && peer.focus) {
      peer.focus();
      return;
    }

    var url = window.location.href.split('#')[0] + '#presenter';
    var opened = window.open(url, 'html-deck-presenter', 'popup,width=1280,height=800');
    if (opened) {
      peer = opened;
    }
  }

  var panel = null;
  var started = Date.now();

  function element(tag, className, parent) {
    var created = document.createElement(tag);
    if (className) {
      created.className = className;
    }

    if (parent) {
      parent.appendChild(created);
    }

    return created;
  }

  function setUpPresenter() {
    body.classList.add('presenter');
    document.title = 'Presenter: ' + document.title;
    panel = element('div', null, body);
    panel.id = 'presenter';
    var side = element('div', 'presenter-side', panel);
    element('h2', null, side).textContent = 'Next';
    panel.nextView = element('div', 'presenter-next', side);
    var status = element('div', 'presenter-status', side);
    panel.position = element('div', 'presenter-position', status);
    var clock = element('div', 'presenter-clock', status);
    panel.time = element('span', 'presenter-time', clock);
    var reset = element('button', null, clock);
    reset.type = 'button';
    reset.textContent = 'Reset';
    reset.addEventListener('click', function () {
      started = Date.now();
      tick();
    });
    panel.notes = element('div', 'presenter-notes', panel);
    tick();
    window.setInterval(tick, 1000);
  }

  function tick() {
    if (!panel) {
      return;
    }

    var seconds = Math.floor((Date.now() - started) / 1000);
    var two = function (n) {
      return (n < 10 ? '0' : '') + n;
    };
    panel.time.textContent = (seconds >= 3600 ? Math.floor(seconds / 3600) + ':' : '') + two(Math.floor(seconds / 60) % 60) + ':' + two(seconds % 60);
  }

  // The presenter window: the next step (the same slide one step on, else the next slide), the notes, the position.
  function updatePresenter() {
    if (!panel) {
      return;
    }

    var total = info[current].steps.length;
    panel.position.textContent = 'Slide ' + (current + 1) + ' of ' + slides.length + (total > 0 ? ', step ' + step + ' of ' + total : '');
    while (panel.nextView.firstChild) {
      panel.nextView.removeChild(panel.nextView.firstChild);
    }

    var nextSlide = step < total ? current : current + 1;
    if (nextSlide < slides.length) {
      var copy = slides[nextSlide].querySelector('svg').cloneNode(true);
      render(copy, info[nextSlide].steps, nextSlide === current ? step + 1 : 0);
      panel.nextView.appendChild(copy);
    } else {
      element('p', 'presenter-end', panel.nextView).textContent = 'End of the deck';
    }

    while (panel.notes.firstChild) {
      panel.notes.removeChild(panel.notes.firstChild);
    }

    var notes = slides[current].querySelector('aside.notes');
    if (notes) {
      Array.prototype.forEach.call(notes.childNodes, function (node) {
        panel.notes.appendChild(node.cloneNode(true));
      });
    }
  }

  // ---- Input ------------------------------------------------------------------------------------------------------

  document.addEventListener('keydown', function (event) {
    if (event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    var key = event.key;
    var inOverview = body.classList.contains('overview');
    if (/^[0-9]$/.test(key)) {
      typed += key;
      return;
    }

    if (key === 'Escape') {
      typed = '';
      toggleHelp(false);
      toggleBlack(false);
      if (inOverview) {
        overview(false);
      }

      return;
    }

    if (body.classList.contains('black') && key !== 'b' && key !== 'B' && key !== '.') {
      toggleBlack(false);
    }

    if (inOverview) {
      var columns = Math.max(1, Math.round(deck.clientWidth / slides[0].clientWidth));
      var arrows = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: columns, ArrowUp: -columns };
      if (key in arrows) {
        select(selected + arrows[key]);
        event.preventDefault();
        return;
      }

      if (key === 'Enter' || key === ' ') {
        body.classList.remove('overview');
        go(selected, 0);
        event.preventDefault();
        return;
      }
    }

    switch (key) {
      case 'ArrowRight':
      case 'ArrowDown':
      case 'PageDown':
      case ' ':
        next();
        break;
      case 'Enter':
        if (typed) {
          go(parseInt(typed, 10) - 1, 0);
        } else {
          next();
        }

        break;
      case 'ArrowLeft':
      case 'ArrowUp':
      case 'PageUp':
      case 'Backspace':
        previous();
        break;
      case 'Home':
        go(0, 0);
        break;
      case 'End':
        go(slides.length - 1, 0);
        break;
      case 'f':
      case 'F':
        fullScreen();
        break;
      case 'o':
      case 'O':
        overview(!inOverview);
        break;
      case 'b':
      case 'B':
      case '.':
        toggleBlack(!body.classList.contains('black'));
        break;
      case 'n':
      case 'N':
        if (!presenterMode) {
          toggleNotes(!body.classList.contains('notes-view'));
        }

        break;
      case 'p':
      case 'P':
        openPresenter();
        break;
      case '?':
        toggleHelp(help.hidden);
        break;
      default:
        typed = '';
        return;
    }

    typed = '';
    event.preventDefault();
  });

  document.addEventListener('click', function (event) {
    if (event.target.closest('a') || event.target.closest('#help') || event.target.closest('.bar')
      || event.target.closest('#presenter') || event.target.closest('aside.notes')) {
      return;
    }

    if (body.classList.contains('black')) {
      toggleBlack(false);
      return;
    }

    var slide = event.target.closest('section.slide');
    if (body.classList.contains('overview')) {
      if (slide) {
        body.classList.remove('overview');
        go(slides.indexOf(slide), 0);
      }

      return;
    }

    // Text stays selectable: a click that selected something does not move on.
    if (window.getSelection && String(window.getSelection()).length > 0) {
      return;
    }

    next();
  });

  var touchX = null;
  document.addEventListener('touchstart', function (event) {
    touchX = event.touches.length === 1 ? event.touches[0].clientX : null;
  }, { passive: true });
  document.addEventListener('touchend', function (event) {
    if (touchX === null || body.classList.contains('overview')) {
      return;
    }

    var dx = event.changedTouches[0].clientX - touchX;
    touchX = null;
    if (Math.abs(dx) > 50) {
      if (dx < 0) {
        next();
      } else {
        previous();
      }
    }
  });

  document.getElementById('help-button').addEventListener('click', function () {
    toggleHelp(help.hidden);
  });

  window.addEventListener('hashchange', function () {
    if (!presenterMode) {
      var target = fromHash();
      if (target.slide !== current || target.step !== step) {
        go(target.slide, target.step);
      }
    }
  });

  // Print shows every slide in its final build state through the styles (build-gone); a running effect ends first.
  window.addEventListener('beforeprint', settle);

  body.classList.add('scripted');
  slides.forEach(function (slide, i) {
    render(slide, info[i].steps, info[i].steps.length);
  });
  if (presenterMode) {
    setUpPresenter();
    go(0, 0, null, true);
    send({ kind: 'hello' });
  } else {
    var start = fromHash();
    current = start.slide;
    go(start.slide, start.step, null, true);
  }
})();

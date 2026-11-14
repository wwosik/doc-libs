// The HTML deck's one script: moving through the slides, the overview, the black screen and the key list.
// Plain JavaScript, not minified, so it can be reviewed like code. E07-S06 adds builds, transitions and the notes.
(function () {
  'use strict';

  var slides = Array.prototype.slice.call(document.querySelectorAll('section.slide'));
  var body = document.body;
  var counter = document.getElementById('counter');
  var help = document.getElementById('help');
  var current = 0;
  var selected = 0;
  var typed = '';

  function fromHash() {
    var match = /^#(\d+)$/.exec(window.location.hash);
    var number = match ? parseInt(match[1], 10) : 1;
    return Math.min(Math.max(number, 1), slides.length) - 1;
  }

  function show(index) {
    if (slides.length === 0) {
      return;
    }

    current = Math.min(Math.max(index, 0), slides.length - 1);
    slides.forEach(function (slide, i) {
      slide.classList.toggle('current', i === current);
      slide.setAttribute('aria-hidden', i === current || body.classList.contains('overview') ? 'false' : 'true');
    });
    counter.textContent = (current + 1) + ' / ' + slides.length;
    var hash = '#' + (current + 1);
    if (window.location.hash !== hash) {
      try {
        window.history.replaceState(null, '', hash);
      } catch (e) {
        // A page opened from disk in some browsers refuses history changes; the slide still shows.
      }
    }
  }

  function select(index) {
    selected = Math.min(Math.max(index, 0), slides.length - 1);
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

    show(current);
  }

  function toggleBlack(on) {
    body.classList.toggle('black', on);
  }

  function toggleHelp(on) {
    help.hidden = !on;
  }

  function fullScreen() {
    var element = document.documentElement;
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else if (element.requestFullscreen) {
      element.requestFullscreen();
    }
  }

  function next() {
    show(current + 1);
  }

  function previous() {
    show(current - 1);
  }

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
      overview(false);
      return;
    }

    if (body.classList.contains('black') && key !== 'b' && key !== 'B' && key !== '.') {
      toggleBlack(false);
    }

    if (inOverview) {
      var columns = Math.max(1, Math.round(document.getElementById('deck').clientWidth / slides[0].clientWidth));
      var moves = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: columns, ArrowUp: -columns };
      if (key in moves) {
        select(selected + moves[key]);
        event.preventDefault();
        return;
      }

      if (key === 'Enter' || key === ' ') {
        current = selected;
        overview(false);
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
          show(parseInt(typed, 10) - 1);
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
        show(0);
        break;
      case 'End':
        show(slides.length - 1);
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
    if (event.target.closest('a') || event.target.closest('#help') || event.target.closest('.bar')) {
      return;
    }

    if (body.classList.contains('black')) {
      toggleBlack(false);
      return;
    }

    var slide = event.target.closest('section.slide');
    if (body.classList.contains('overview')) {
      if (slide) {
        current = slides.indexOf(slide);
        overview(false);
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
    show(fromHash());
  });

  body.classList.add('scripted');
  show(fromHash());
})();

(function () {
    var buttons = Array.from(document.querySelectorAll('[data-install-app]'));
    if (!buttons.length) return;

    var installPrompt = null;
    var toast = document.querySelector('.toast');

    function isInstalled() {
        return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    }

    function setVisible(visible) {
        buttons.forEach(function (button) { button.hidden = !visible; });
    }

    function showMessage(message) {
        if (toast) {
            toast.textContent = message;
            toast.classList.add('show');
            window.setTimeout(function () { toast.classList.remove('show'); }, 3500);
        } else {
            window.alert(message);
        }
    }

    setVisible(!isInstalled());

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();
        installPrompt = event;
        setVisible(!isInstalled());
    });

    window.addEventListener('appinstalled', function () {
        installPrompt = null;
        setVisible(false);
        showMessage('NEET BEE was installed successfully.');
    });

    buttons.forEach(function (button) {
        button.addEventListener('click', async function () {
            if (!installPrompt) {
                showMessage('To install NEET BEE, use your browser menu and choose “Install app” or “Add to Home Screen”.');
                return;
            }

            installPrompt.prompt();
            var choice = await installPrompt.userChoice;
            installPrompt = null;
            if (choice && choice.outcome === 'accepted') setVisible(false);
        });
    });

    if ('serviceWorker' in navigator && window.location.protocol !== 'file:') {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js').catch(function (error) {
                console.error('NEET BEE service worker registration failed:', error);
            });
        });
    }
})();

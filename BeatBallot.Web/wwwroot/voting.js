class SongRequestApp {
    constructor() {
        const path = window.location.pathname;
        const segments = path.split('/');
        this.sessionId = segments[segments.length - 1] || segments[segments.length - 2];
        this.isSearching = false;
        this.isSubmitting = false;
        this.searchResults = [];
        this.requestedSongs = [];
        this.searchTimer = null;
        this.userName = '';
        this.deviceInfo = {};
        this.initializeElements();
        this.bindEvents();
        this.initialize();
    }

    getBrowserName() {
        const userAgent = navigator.userAgent;

        if (userAgent.includes('Edg/')) return 'Edge';
        if (userAgent.includes('Chrome/')) return 'Chrome';
        if (userAgent.includes('Firefox/')) return 'Firefox';
        if (userAgent.includes('Safari/') && !userAgent.includes('Chrome')) return 'Safari';
        if (userAgent.includes('Opera') || userAgent.includes('OPR/')) return 'Opera';

        return 'Unknown';
    }

    // Function to detect if mobile
    getIsMobile() {
        return /Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    }


    async checkIsDeviceInSession(sessionId, deviceId) {
        try {
            const response = await fetch(`/api/listenerrequest/checkIsDeviceInSession?sessionId=${sessionId}&deviceId=${deviceId}`, {
                method: 'GET'
            });

            if (!response.ok) {
                throw new Error('Failed to register device');
            }
            else {

                let result = await response.json();
                return result.inSession;
            }
        }
        finally {

        }
    }

    // Function to register device and get DeviceID
    async registerDeviceAndGetId(sessionId) {
        try {
            // Check if we already have a device ID
            let deviceId = localStorage.getItem('beatballot_device_id');

            if (deviceId) {
                return deviceId;
            }

            const deviceInfo = {};

            deviceInfo.sessionId = sessionId;
            deviceInfo.userAgent = navigator.userAgent;
            deviceInfo.browser = this.getBrowserName();
            deviceInfo.screenSize = `${screen.width}x${screen.height}`;
            deviceInfo.isMobile = this.getIsMobile();
            deviceInfo.currentDateTime = new Date().toISOString();


            const response = await fetch('/api/listenerrequest/registerdevice', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(deviceInfo)
            });

            if (!response.ok) {
                throw new Error('Failed to register device');
            }


            const result = await response.json();
            if (result.success && result.deviceId) {
                localStorage.setItem('beatballot_device_id', result.deviceId);
                return result.deviceId;
            } else {
                throw new Error('Invalid response from server');
            }

        } catch (error) {
            console.error('Error registering device:', error);
            // Return a fallback device ID if registration fails
            const fallbackId = 'device_fallback_' + Date.now();
            //localStorage.setItem('beatballot_device_id', fallbackId);
            return fallbackId;
        }
    }



    initializeElements() {
        this.elements = {
            loadingState: document.getElementById('loading-state'),
            mainContent: document.getElementById('main-content'),
            searchInput: document.getElementById('search-input'),
            clearSearchBtn: document.getElementById('clear-search-btn'),
            searchLoading: document.getElementById('search-loading'),
            autocompleteResults: document.getElementById('autocomplete-results'),
            requestedSection: document.getElementById('requested-section'),
            requestedSongsList: document.getElementById('requested-songs-list'),
            nameInput: document.getElementById('name-input'),
            clearNameBtn: document.getElementById('clear-name-btn'),
            namePreview: document.getElementById('name-preview'),
            submitBtn: document.getElementById('submit-requests-btn'),
            submitText: document.getElementById('submit-text'),
            emptyState: document.getElementById('empty-state'),
            successOverlay: document.getElementById('success-overlay'),
            successMessage: document.getElementById('success-message'),
            duplicateInfo: document.getElementById('duplicate-info'),
            closeSuccessBtn: document.getElementById('close-success-btn'),
            errorOverlay: document.getElementById('error-overlay'),
            errorMessage: document.getElementById('error-message'),
            closeErrorBtn: document.getElementById('close-error-btn')
        };
    }

    bindEvents() {
        // Search events
        this.elements.searchInput.addEventListener('input', (e) => this.onSearchInput(e));
        this.elements.searchInput.addEventListener('keypress', (e) => this.onKeyPress(e));
        this.elements.clearSearchBtn.addEventListener('click', () => this.clearSearch());

        // Name events
        this.elements.nameInput.addEventListener('input', (e) => this.onNameInput(e));
        this.elements.clearNameBtn.addEventListener('click', () => this.clearName());

        // Submit button
        this.elements.submitBtn.addEventListener('click', () => this.submitRequests());

        // Modal events
        this.elements.closeSuccessBtn.addEventListener('click', () => this.hideSuccessMessage());
        this.elements.closeErrorBtn.addEventListener('click', () => this.hideErrorMessage());
        this.elements.successOverlay.addEventListener('click', () => this.hideSuccessMessage());
        this.elements.errorOverlay.addEventListener('click', () => this.hideErrorMessage());

        // Prevent modal close when clicking inside modal
        document.querySelector('.success-modal').addEventListener('click', (e) => e.stopPropagation());
    }

    async initialize() {
        if (!this.sessionId) {
            window.location.href = '/';
            return;
        }

        try {
            // Validate session exists
            this.deviceId = await this.registerDeviceAndGetId(this.sessionId);
            let isInSession = await this.checkIsDeviceInSession(this.sessionId, this.deviceId);

            if (!isInSession) {
                await this.addDeviceToSession(this.sessionId, this.deviceId);


            }

            await this.validateSession();
            this.elements.loadingState.classList.add('hidden');
            this.elements.mainContent.classList.remove('hidden');
            this.updateUI();
        } catch (error) {
            console.error('Initialization error:', error);
            this.showError('Failed to initialize. Please refresh the page.');
        }
    }

    async addDeviceToSession(sessionId, deviceId) {
        try {
            const response = await fetch(`/api/listenerrequest/addDeviceToSession/?sessionId=${sessionId}&deviceId=${deviceId}`, {
                method: 'POST'
            });

            if (!response.ok) {
                throw new Error('Adding device to session failed');
            }
            const result = await response.json();
        } catch (error) {
            console.error('Session validation error:', error);
            this.showError('This session is not currently accepting requests.');
            throw error;
        }
    }

    async validateSession() {
        try {
            const response = await fetch(`/api/listenerrequest/validatesession/${this.sessionId}`, {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error('Session validation failed');
            }
            const result = await response.json(); // 👈 FIXED: Parse JSON response

            if (!result.isActive) {
                throw new Error('Session is not active');
            }
        } catch (error) {
            console.error('Session validation error:', error);
            this.showError('This session is not currently accepting requests.');
            throw error;
        }
    }

    onSearchInput(e) {
        const query = e.target.value;

        if (this.searchTimer) {
            clearTimeout(this.searchTimer);
        }

        this.elements.clearSearchBtn.classList.toggle('hidden', !query);

        if (!query.trim()) {
            this.searchResults = [];
            this.updateSearchResults();
            return;
        }

        this.searchTimer = setTimeout(() => {
            this.performSearch(query);
        }, 250);
    }

    onKeyPress(e) {
        if (e.key === 'Enter' && this.elements.searchInput.value.trim()) {
            if (this.searchTimer) {
                clearTimeout(this.searchTimer);
            }
            this.performSearch(this.elements.searchInput.value);
        }
    }

    async performSearch(query) {
        if (!query.trim()) return;

        this.isSearching = true;
        this.elements.searchLoading.classList.remove('hidden');

        try {
            const response = await fetch(`/api/listenerrequest/search/${this.sessionId}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    query: query,
                    sessionId: this.sessionId,
                    limit: 20
                })
            });

            if (!response.ok) {
                throw new Error('Search request failed');
            }

            const result = await response.json();
            this.searchResults = result.tracks || [];
        } catch (error) {
            console.error('Search error:', error);
            this.searchResults = [];
            this.showError('Search failed.');
        }

        this.isSearching = false;
        this.elements.searchLoading.classList.add('hidden');
        this.updateSearchResults();
    }

    updateSearchResults() {
        const container = this.elements.autocompleteResults;


        if (this.searchResults.length === 0 || !this.elements.searchInput.value.trim()) {
            container.classList.add('hidden');
            return;
        }

        container.innerHTML = '';
        container.classList.remove('hidden');



        this.searchResults.slice(0, 8).forEach(track => {
            const isRequested = this.isTrackRequested(track.id);
            const item = document.createElement('div');
            item.className = `search-result-item ${isRequested ? 'already-requested' : ''}`;



            item.innerHTML = `
                        <div class="track-cover">
                            ${track.albumImageUrl
                    ? `<img src="${track.albumImageUrl}" alt="${track.name}" class="cover-image" />`
                    : '<div class="cover-placeholder">🎵</div>'
                }
                        </div>
                        <div class="track-info">
                            <p class="track-name">${track.name}${track.isExplicit ? ' (E)' : ''}</p>
                            <p class="track-artist">${track.artistNames}</p>
                            <p class="track-album">${track.albumName || ''}</p>
                        </div>
                        <div class="track-actions">
                            ${isRequested
                    ? '<span class="requested-badge">✓ Requested</span>'
                    : '<button class="request-btn"><span class="btn-icon">+</span><span>Add to Bag</span></button>'
                }
                        </div>
                    `;

            if (!isRequested) {
                item.addEventListener('click', () => this.requestTrack(track));
            }

            container.appendChild(item);
        });
    }

    clearSearch() {
        this.elements.searchInput.value = '';
        this.elements.clearSearchBtn.classList.add('hidden');
        this.searchResults = [];
        this.updateSearchResults();
    }

    onNameInput(e) {
        this.userName = e.target.value;
        this.elements.clearNameBtn.classList.toggle('hidden', !this.userName);
        this.elements.namePreview.textContent = this.userName.trim() || 'Anonymous';
    }

    clearName() {
        this.userName = '';
        this.elements.nameInput.value = '';
        this.elements.clearNameBtn.classList.add('hidden');
        this.elements.namePreview.textContent = 'Anonymous';
    }

    isTrackRequested(trackId) {
        return this.requestedSongs.some(r => r.song.id === trackId);
    }

    requestTrack(track) {
        if (this.isTrackRequested(track.id)) return;
        const request = {
            song: track.fullTrack || track,
            requestedAt: new Date(),
            sessionId: this.sessionId
        };

        this.requestedSongs.push(request);
        this.clearSearch();
        this.updateUI();
    }

    removeRequest(index) {
        this.requestedSongs.splice(index, 1);
        this.updateUI();
    }

    updateUI() {
        this.updateRequestedSongs();
        this.updateSubmitButton();
        this.updateEmptyState();
        this.updateSearchResults(); // Refresh to update requested badges
    }

    updateRequestedSongs() {
        const hasRequests = this.requestedSongs.length > 0;
        this.elements.requestedSection.classList.toggle('hidden', !hasRequests);
        if (hasRequests) {
            this.elements.requestedSongsList.innerHTML = '';
            this.requestedSongs.forEach((request, index) => {
                const item = document.createElement('div');
                item.className = 'requested-song-item';
                const requestTime = new Date(request.requestedAt).toLocaleTimeString('en-US', {
                    hour: '2-digit',
                    minute: '2-digit',
                    hour12: false
                });

                const song = request.song;
                const displayName = song.name || song.Name;
                const displayArtists = song.artistNames ||
                    (song.Artists ? song.Artists.map(a => a.Name).join(', ') : 'Unknown');
                const displayAlbum = song.albumName || song.Album?.Name || '';
                const displayImage = song.albumImageUrl ||
                    (song.album?.images && song.album.images.length > 0 ? song.album.images[0].url : null);

                item.innerHTML = `
                <div class="song-position">${index + 1}</div>
                <div class="song-cover">
                    ${displayImage
                        ? `<img src="${displayImage}" alt="${displayName}" class="cover-image" />`
                        : '<div class="cover-placeholder">🎵</div>'
                    }
                </div>
                <div class="song-info">
                    <h3 class="song-name">${displayName}</h3>
                    <p class="song-artist">${displayArtists}</p>
                    <p class="song-album">${displayAlbum}</p>
                    <span class="request-time">Requested ${requestTime}</span>
                </div>
                <div class="song-actions">
                    <button class="remove-btn">×</button>
                </div>
            `;

                item.querySelector('.remove-btn').addEventListener('click', () => this.removeRequest(index));
                this.elements.requestedSongsList.appendChild(item);
            });
        }
    }


    updateSubmitButton() {
        const count = this.requestedSongs.length;
        this.elements.submitText.textContent = this.isSubmitting
            ? 'Submitting...'
            : `Submit ${count} Request${count !== 1 ? 's' : ''}`;

        this.elements.submitBtn.disabled = this.isSubmitting || count === 0;

        if (this.isSubmitting) {
            this.elements.submitBtn.innerHTML = '<span class="loading-spinner"></span><span>Submitting...</span>';
        } else {
            this.elements.submitBtn.innerHTML = `<span>Submit ${count} Request${count !== 1 ? 's' : ''}</span>`;
        }
    }

    updateEmptyState() {
        const hasRequests = this.requestedSongs.length > 0;
        const hasSearch = this.elements.searchInput.value.trim();
        this.elements.emptyState.classList.toggle('hidden', hasRequests || hasSearch);
    }

    async submitRequests() {
        if (this.requestedSongs.length === 0 || this.isSubmitting) return;

        this.isSubmitting = true;
        this.updateSubmitButton();

        try {
            const simpleSongRequest = {
                sessionId: this.sessionId,
                songs: this.requestedSongs.map(r => ({
                    id: r.song.id,
                    name: r.song.name,
                    uri: r.song.uri,
                    artistNames: r.song.artistNames,
                    albumName: r.song.albumName,
                    albumImageUrl: r.song.albumImageUrl || (r.song.album?.images && r.song.album.images.length > 0 ? r.song.album.images[0].url : null),
                    explicit: r.song.explicit,
                    durationMs: r.song.durationMs
                })),
                requestedAt: new Date().toISOString(),
                requesterName: this.userName.trim() || 'Anonymous',
                deviceId: await this.deviceId
            };

            const response = await fetch('/api/listenerrequest/submit', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(simpleSongRequest)
            });

            if (!response.ok) {
                throw new Error('Failed to submit requests');
            }

            const result = await response.json();
            this.handleRequestsSubmitted(result);
        } catch (error) {
            console.error('Submit error:', error);
            this.showError('Failed to submit requests. Please try again.');
            this.isSubmitting = false;
            this.updateSubmitButton();
        }
    }

    handleRequestsSubmitted(result) {
        this.isSubmitting = false;
        this.updateSubmitButton();

        if (result.success) {
            const count = result.songsAdded;
            const duplicates = result.duplicateSongs?.length || 0;

            this.elements.successMessage.textContent =
                `Your ${count} song request${count !== 1 ? 's have' : ' has'} been sent to the DJ.`;

            if (duplicates > 0) {
                this.elements.duplicateInfo.textContent =
                    `(${duplicates} duplicate${duplicates !== 1 ? 's' : ''} ignored)`;
                this.elements.duplicateInfo.classList.remove('hidden');
            } else {
                this.elements.duplicateInfo.classList.add('hidden');
            }

            this.requestedSongs = [];
            this.updateUI();
            this.elements.successOverlay.classList.remove('hidden');
        } else {
            this.showError(result.message || 'Unknown error occurred');
        }
    }

    showError(message) {
        this.elements.errorMessage.textContent = message;
        this.elements.errorOverlay.classList.remove('hidden');
    }

    hideSuccessMessage() {
        this.elements.successOverlay.classList.add('hidden');
    }

    hideErrorMessage() {
        this.elements.errorOverlay.classList.add('hidden');
    }
}

// Initialize the app when the page loads
document.addEventListener('DOMContentLoaded', () => {
    new SongRequestApp();
});
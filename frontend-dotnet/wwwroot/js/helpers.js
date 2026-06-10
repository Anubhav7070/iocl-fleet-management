
/**
 * IOCL Fleet – JS Helpers
 * Handles cross-origin file downloads using fetch + Blob URL,
 * because the HTML `download` attribute is blocked by browsers for
 * cross-origin URLs (uploads served from a different port/origin).
 */
window.ioclHelpers = {

    /**
     * Download a file from any URL by fetching it as a blob and
     * triggering a synthetic anchor click – works cross-origin.
     * @param {string} url       - Full URL of the file to download
     * @param {string} fileName  - Suggested filename for the saved file
     * @param {string} token     - Optional Bearer token for authenticated endpoints
     */
    downloadFile: async function (url, fileName, token) {
        try {
            const headers = {};
            if (token) {
                headers['Authorization'] = 'Bearer ' + token;
            }

            const response = await fetch(url, { headers });
            if (!response.ok) {
                console.error('[ioclHelpers] Download failed:', response.status, response.statusText);
                return;
            }

            const blob = await response.blob();
            const blobUrl = URL.createObjectURL(blob);

            const anchor = document.createElement('a');
            anchor.href = blobUrl;
            anchor.download = fileName || 'download';
            anchor.style.display = 'none';
            document.body.appendChild(anchor);
            anchor.click();

            // Clean up
            setTimeout(() => {
                URL.revokeObjectURL(blobUrl);
                document.body.removeChild(anchor);
            }, 500);
        } catch (err) {
            console.error('[ioclHelpers] downloadFile error:', err);
        }
    }
};

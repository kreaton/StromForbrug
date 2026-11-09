// localStorage can throw (private windows, blocked site data), so every access is guarded.
window.stromForbrug = {
    storageGet: function (key) {
        try { return window.localStorage.getItem(key); } catch { return null; }
    },
    storageSet: function (key, value) {
        try { window.localStorage.setItem(key, value); } catch { /* ignore */ }
    },
    storageRemove: function (key) {
        try { window.localStorage.removeItem(key); } catch { /* ignore */ }
    },
    // Saves text as a file. The BOM makes Excel read æøå correctly.
    download: function (fileName, text) {
        const blob = new Blob(["﻿" + text], { type: "text/csv;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};

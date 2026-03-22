window.planeventDisplay = {
    getSettings: function () {
        const theme = localStorage.getItem("planevent.theme") || "color";
        const scalePercentRaw = parseInt(localStorage.getItem("planevent.scalePercent") || "100", 10);
        const scalePercent = Number.isFinite(scalePercentRaw) ? Math.min(500, Math.max(50, scalePercentRaw)) : 100;

        return {
            theme: theme,
            scalePercent: scalePercent
        };
    },

    applySettings: function (theme, scalePercent) {
        const safeTheme = (theme || "color").toLowerCase();
        const parsedScale = parseInt(scalePercent, 10);
        const safeScalePercent = Number.isFinite(parsedScale) ? Math.min(500, Math.max(50, parsedScale)) : 100;
        const zoomFactor = 100 / safeScalePercent;

        document.documentElement.setAttribute("data-theme", safeTheme);
        document.documentElement.style.setProperty("--app-scale-factor", zoomFactor.toString());
        document.documentElement.style.setProperty("--app-scale-percent", safeScalePercent.toString());

        localStorage.setItem("planevent.theme", safeTheme);
        localStorage.setItem("planevent.scalePercent", safeScalePercent.toString());
    }
};

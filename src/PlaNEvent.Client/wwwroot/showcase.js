window.showcaseUi = {
    scrollRail: function (railId, direction) {
        const rail = document.getElementById(railId);
        if (!rail) {
            return;
        }

        const amount = Math.max(rail.clientWidth * 0.82, 280);
        rail.scrollBy({
            left: amount * direction,
            behavior: "smooth"
        });
    }
};

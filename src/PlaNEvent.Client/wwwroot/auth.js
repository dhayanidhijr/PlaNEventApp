window.planeventAuth = {
    getToken: function (key) {
        return localStorage.getItem(key) || "";
    },
    setToken: function (key, value) {
        if (!value) {
            localStorage.removeItem(key);
            return;
        }

        localStorage.setItem(key, value);
    }
};

(function () {
    function parseSse(buffer, emitEvent) {
        let boundaryIndex = buffer.indexOf("\n\n");
        while (boundaryIndex >= 0) {
            const rawEvent = buffer.slice(0, boundaryIndex);
            buffer = buffer.slice(boundaryIndex + 2);

            const lines = rawEvent.split(/\r?\n/);
            let eventName = "message";
            const dataLines = [];

            for (const line of lines) {
                if (line.startsWith("event:")) {
                    eventName = line.slice(6).trim();
                } else if (line.startsWith("data:")) {
                    dataLines.push(line.slice(5).trim());
                }
            }

            if (dataLines.length > 0) {
                emitEvent(eventName, dataLines.join("\n"));
            }

            boundaryIndex = buffer.indexOf("\n\n");
        }

        return buffer;
    }

    window.planeventAgentChat = {
        initResize: function (panelId, handleId) {
            const panel = document.getElementById(panelId);
            const handle = document.getElementById(handleId);
            if (!panel || !handle) {
                return;
            }

            const clampWidthPercent = function (value) {
                return Math.min(88, Math.max(30, value));
            };

            const applyWidth = function (widthPercent) {
                const safeWidth = clampWidthPercent(widthPercent);
                panel.style.width = `${safeWidth}%`;
                localStorage.setItem("planevent.agentChat.widthPercent", safeWidth.toString());
            };

            if (!panel.dataset.widthInitialized) {
                const savedWidth = parseFloat(localStorage.getItem("planevent.agentChat.widthPercent") || "60");
                applyWidth(Number.isFinite(savedWidth) ? savedWidth : 60);
                panel.dataset.widthInitialized = "true";
            }

            if (handle.dataset.resizeBound) {
                return;
            }

            handle.dataset.resizeBound = "true";
            handle.addEventListener("mousedown", function (event) {
                event.preventDefault();

                const overlay = panel.parentElement;
                if (!overlay) {
                    return;
                }

                const onMove = function (moveEvent) {
                    const overlayRect = overlay.getBoundingClientRect();
                    const widthPercent = ((overlayRect.right - moveEvent.clientX) / overlayRect.width) * 100;
                    applyWidth(widthPercent);
                };

                const onUp = function () {
                    window.removeEventListener("mousemove", onMove);
                    window.removeEventListener("mouseup", onUp);
                };

                window.addEventListener("mousemove", onMove);
                window.addEventListener("mouseup", onUp);
            });
        },
        initSplitResize: function (shellId, panelId, handleId, storageKey, defaultWidth, minWidth, maxWidth) {
            const shell = document.getElementById(shellId);
            const panel = document.getElementById(panelId);
            const handle = document.getElementById(handleId);
            if (!shell || !panel || !handle) {
                return;
            }

            const clampWidth = function (value) {
                return Math.min(maxWidth || 720, Math.max(minWidth || 340, value));
            };

            const applyWidth = function (widthPx) {
                const safeWidth = clampWidth(widthPx);
                panel.style.width = `${safeWidth}px`;
                shell.style.setProperty("--calendar-sidebar-width", `${safeWidth}px`);
                localStorage.setItem(storageKey || "planevent.calendar.sidebarWidth", safeWidth.toString());
            };

            if (!handle.dataset.splitResizeInitialized) {
                const savedWidth = parseFloat(localStorage.getItem(storageKey || "planevent.calendar.sidebarWidth") || `${defaultWidth || 460}`);
                applyWidth(Number.isFinite(savedWidth) ? savedWidth : (defaultWidth || 460));
                handle.dataset.splitResizeInitialized = "true";
            }

            if (handle.dataset.splitResizeBound) {
                return;
            }

            handle.dataset.splitResizeBound = "true";
            handle.addEventListener("mousedown", function (event) {
                event.preventDefault();

                const onMove = function (moveEvent) {
                    const shellRect = shell.getBoundingClientRect();
                    const widthPx = moveEvent.clientX - shellRect.left;
                    applyWidth(widthPx);
                };

                const onUp = function () {
                    window.removeEventListener("mousemove", onMove);
                    window.removeEventListener("mouseup", onUp);
                };

                window.addEventListener("mousemove", onMove);
                window.addEventListener("mouseup", onUp);
            });
        },
        scrollToBottom: function (elementId) {
            const element = document.getElementById(elementId);
            if (!element) {
                return;
            }

            element.scrollTop = element.scrollHeight;
        },
        stream: async function (url, token, request, dotNetRef) {
            const headers = {
                "Content-Type": "application/json",
                "Accept": "text/event-stream"
            };

            if (token) {
                headers["Authorization"] = `Bearer ${token}`;
            }

            const response = await fetch(url, {
                method: "POST",
                headers,
                body: JSON.stringify(request)
            });

            if (!response.ok) {
                const errorText = await response.text();
                await dotNetRef.invokeMethodAsync("OnAgentStreamEvent", "error", JSON.stringify({
                    message: errorText || `Streaming request failed with status ${response.status}.`
                }));
                return;
            }

            if (!response.body) {
                await dotNetRef.invokeMethodAsync("OnAgentStreamEvent", "error", JSON.stringify({
                    message: "Streaming response body was empty."
                }));
                return;
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";

            while (true) {
                const chunk = await reader.read();
                if (chunk.done) {
                    break;
                }

                buffer += decoder.decode(chunk.value, { stream: true });
                buffer = parseSse(buffer, (eventName, data) => {
                    dotNetRef.invokeMethodAsync("OnAgentStreamEvent", eventName, data);
                });
            }

            buffer += decoder.decode();
            parseSse(buffer, (eventName, data) => {
                dotNetRef.invokeMethodAsync("OnAgentStreamEvent", eventName, data);
            });
        }
    };
})();

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

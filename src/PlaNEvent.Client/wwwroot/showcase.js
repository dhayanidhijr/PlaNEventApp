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
    },

    downloadCalendarFile: function (fileName, content) {
        const blob = new Blob([content], { type: "text/calendar;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    },

    printBookingConfirmation: function (title, bodyHtml) {
        const printWindow = window.open("", "_blank", "noopener,noreferrer,width=900,height=700");
        if (!printWindow) {
            return;
        }

        printWindow.document.write(`
            <html>
            <head>
                <title>${title}</title>
                <style>
                    body { font-family: Arial, sans-serif; margin: 32px; color: #122033; }
                    h1 { margin-bottom: 12px; }
                    .card { border: 1px solid #d7dfef; border-radius: 16px; padding: 20px; }
                    .meta { margin: 10px 0; font-size: 16px; }
                </style>
            </head>
            <body>
                <h1>${title}</h1>
                <div class="card">${bodyHtml}</div>
            </body>
            </html>`);
        printWindow.document.close();
        printWindow.focus();
        printWindow.print();
    }
};

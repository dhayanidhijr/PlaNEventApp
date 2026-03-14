import os

from planevent_sage_agent.app import app


if __name__ == "__main__":
    port = int(os.getenv("PORT", "8080"))
    app.run(port=port, host="0.0.0.0")

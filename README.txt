# BCP QR Parking System — Python Backend

## 1. Install Python
Install Python 3.11+ on Windows and make sure `python` works in Command Prompt.

## 2. Start the API
Double-click `start-server.bat`.

The API will run at:
http://127.0.0.1:8000

API documentation:
http://127.0.0.1:8000/docs

## 3. Open the website
Open `bcp-parking-system.html` in your browser.

The HTML now reads and saves the application state through the Python FastAPI server.
The server stores the state in `bcp_parking.db` using SQLite.

## 4. Test
Open:
http://127.0.0.1:8000/api/health

You should see:
{"status":"ok","database":".../bcp_parking.db"}

Keep the black server window running while using the website.

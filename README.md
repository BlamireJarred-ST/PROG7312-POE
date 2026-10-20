# PROG7312-POE

## Video Presentation
Part 1 Presentation: https://youtu.be/mrvnyzWNOt4

Part 2 Presentation: https://youtu.be/8whVL1w-LXU

# SmartX

SmartX is a smart sensor monitoring system developed using C#, .NET 10, ASP.NET Core Minimal APIs and WPF.
The system allows sensors to be registered, monitored and tested through a WPF desktop application and REST API.

# Project Structure

The solution contains three projects:

- SmartX.Shared – Shared models and services
- SmartX.API – ASP.NET Core Minimal API backend
- SmartX.Wpf – WPF desktop application

# Main Features

Part 1 — Sensor Data Ingestion and Telemetry
- Register sensors and validate required details.
- Detect duplicate MAC addresses.
- Retrieve registered sensor information through the API.
- Simulate telemetry for supported sensor readings.
- Process telemetry batches.
- Evaluate telemetry severity and display warning/critical conditions.
- Display system health information.
- Attach sensor-related information and validate deployment structures.

Part 2 — Real-Time Command Stream and History
- Process incoming telemetry through background workers.
- Queue ordinary telemetry in arrival order and prioritise critical packets.
- Maintain current device state and telemetry history.
- Review alerts and suppress duplicate alerts for ongoing error conditions.
- Issue manual override commands.
- Track command history and undo supported commands.
- Search and filter devices and activity.
- Display suggested actions and automated insights.
- Pause and resume telemetry pipeline processing and inspect pipeline status.

## Requirements
- Windows 10/11 (the WPF app is Windows-only)
- .NET 10 SDK, and Visual Studio with the ".NET desktop" and "ASP.NET" workloads (or just the `dotnet` CLI)
- Internet access (login uses Firebase Authentication)
- A test account (email + password) provided with the submission
- Optional: Docker Desktop to run the API in a container

## Run (Visual Studio)
1. Clone the repo 
2. Start the API: `dotnet run --project SmartX.API --launch-profile https`
   (Swagger: https://localhost:7078/swagger)
3. In a second terminal start the app: `dotnet run --project SmartX.Wpf`
   (In Visual Studio you can instead set Multiple startup projects: SmartX.API and SmartX.Wpf.)
4. Sign in with the test account.

## Run the API in Docker (optional)
1. `docker compose up --build`  (API on http://localhost:8080, Swagger at /swagger)
2. Point the app at it before starting it (PowerShell):
   `$env:SMARTX_API_URL = "http://localhost:8080"; dotnet run --project SmartX.Wpf`

## Login credentials
User@test.com
UserTest123!

## Using the app
- Sensor Data Ingestion and Telemetry: press Start Simulation so telemetry flows.
- Real-Time Command Stream and History: select a device, issue Pump/Relay/Valve commands, press Undo.
- Network Topology and Mesh Routing is greyed out (final PoE).
- Switching tabs keeps all data; the simulation keeps running.

## Testing the queues, stack, dictionaries and sets
**Simulate a high-priority alert (Queue vs PriorityQueue):**
1. Open the Command Stream tab and press **Pause Pipeline**.
2. Let a few normal readings arrive (the FIFO queue counter rises).
3. Select NODE-02 and press **Simulate Critical Alert** (the Priority queue counter becomes 1).
4. Press **Resume Pipeline** and open the **Pipeline Order** tab.
   Expected: the PRIORITY packet was processed before the STANDARD packets that arrived earlier.

**Undo (Stack):** issue two commands, open **Undo Stack** (newest on top), press **Undo Last Command**.
The device returns to its previous state. Undo with an empty stack shows "Nothing to undo".

**Dictionary / SortedDictionary:** select a device; its state is found by MAC address and its timeline is
drawn in timestamp order even though the critical packet was processed first.

**HashSet (duplicate suppression):** in Swagger call `POST /api/devices/AA:BB:CC:DD:EE:03/disconnect` twice.
The first returns `raised: true`, the second `duplicateSuppressed: true`; `GET /api/alerts` lists the node once.

**Predictive engine:** press **Seed Demo Patterns**, then **Simulate Critical Alert** on NODE-03.
"Pump ON on NODE-02" appears under Suggested Actions with its confidence.


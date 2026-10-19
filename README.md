# PROG7312-POE

## Video Presentation
Part 1 Presentation: https://youtu.be/mrvnyzWNOt4

Part 2 Presentation: 

# SmartX

# SmartX is a smart sensor monitoring system developed using C#, .NET 10, ASP.NET Core Minimal APIs and WPF.

The system allows sensors to be registered, monitored and tested through a WPF desktop application and REST API.

# Project Structure

## The solution contains three projects:

SmartX.Shared – Shared models and services

SmartX.API – ASP.NET Core Minimal API backend

SmartX.Wpf – WPF desktop application

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

The API runs at:

https://localhost:7078

Swagger is available at:

https://localhost:7078/swagger

The WPF application will open when the solution is run.

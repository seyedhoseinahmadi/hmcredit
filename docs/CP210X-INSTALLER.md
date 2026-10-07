# Offline CP210x driver in HiMate Credit installer

The Windows Setup includes the **unmodified vendor CP210x Universal Windows VCP driver package** so customers do not need to download a driver separately.

## Build pipeline

1. `scripts/stage-cp210x-driver.ps1` downloads `CP210x_Universal_Windows_Driver.zip` from Silicon Labs; when Silicon Labs blocks CI downloads it falls back to the same signed CP210x driver distributed by Espressif for ESP32 development boards.
2. The build requires INF, CAT and SYS files, a matching standard CP2102 hardware ID `USB\\VID_10C4&PID_EA60`, and at least one valid digitally signed driver catalog.
3. The original files are staged into `artifacts/drivers/cp210x`; there is **no third-party driver binary committed to Git**.
4. Inno Setup embeds the driver into the EXE. During installation, the files are extracted to a temporary directory and Windows `pnputil /add-driver <driver> /subdirs /install` is invoked with administrative privileges.
5. Driver files are removed from the temporary directory when Setup completes. Windows may retain the driver in its driver store; uninstalling HiMate Credit does not forcibly remove a shared system driver.

The setup still installs the application in `C:\Program Files\HiMate\Credit`.

**Source:** https://www.silabs.com/software-and-tools/usb-to-uart-bridge-vcp-drivers

**Vendor software terms:** https://www.silabs.com/about-us/legal/terms-and-conditions

Check the driver archive's own terms before customer distribution. Do not modify or re-sign vendor driver packages without performing all relevant vendor/Windows requirements.

## Validation

Test on two Windows machines:
- Fresh Windows where a connected CP2102 is listed as `Other devices` with a yellow warning icon. Run Setup as admin, confirm `Ports (COM & LPT)` gets `Silicon Labs CP210x USB to UART Bridge (COMx)`.
- A machine where CP210x is already working. Setup should preserve the working driver and the app should detect COM.

GitHub Actions CI checks packaging, but cannot test device installation without a physical CP2102 connected to the runner.

**Official Espressif mirror:** https://dl.espressif.com/dl/idf-installer/CP210x_Universal_Windows_Driver.zip

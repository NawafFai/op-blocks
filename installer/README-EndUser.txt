================================================================
  ONE PROCESS Blocks  -  v1.0
  Custom CAPE-OPEN unit operations for Aspen Plus V14
  Designed & developed by Engineer Nawaf - ONE PROCESS Simulation
================================================================

WHAT THIS IS
  25 process-simulation blocks (desalination, membranes, electrochemical,
  lithium/sorption, energy & gas) that plug into Aspen Plus as native
  CAPE-OPEN unit operations, plus a Manager app to install them.

REQUIREMENTS
  - Windows 10/11 (64-bit)
  - .NET Framework 4.8 (already on Windows 10/11)
  - Aspen Plus V14 (to use the blocks)
  The Manager itself is self-contained (no .NET install needed).

----------------------------------------------------------------
INSTALL - pick ONE of the two ways you received it:

  A) Installer  (OPBlocks_Setup.exe)
     1. Run OPBlocks_Setup.exe and approve the Administrator prompt.
        It registers all 25 blocks (x64 + x86) automatically.
     2. Open "ONE PROCESS Blocks Manager" from the Start menu.

  B) Portable ZIP  (OPBlocks-1.1.3-portable.zip)
     1. Right-click the ZIP > Properties > tick "Unblock" > OK
        (this clears Windows' "downloaded file" mark - important).
     2. Extract the ZIP anywhere (e.g. your Desktop).
     3. Double-click  INSTALL.bat  and approve the Administrator prompt.
        It unblocks the files and registers all 25 blocks, then prints
        SUCCESS.
     * To remove them later, double-click UNINSTALL.bat.
     * You can also open OPBlocksManager.exe to manage them with a UI.
----------------------------------------------------------------

USE IN ASPEN PLUS V14
  1. Open Aspen Plus V14 and start a simulation (any template).
  2. If the CAPE-OPEN tab is not shown: Customize > Manage Libraries >
     tick "CAPE-OPEN".
  3. Model Palette > CAPE-OPEN tab > drag any OP-... block onto the
     flowsheet, connect streams, and run.
  (A ready "ONE PROCESS" palette file is included under templates\.)

UNINSTALL
  - Installer:  Windows Settings > Apps > "ONE PROCESS Blocks" > Uninstall.
  - Portable:   double-click UNINSTALL.bat.

TROUBLESHOOTING
  - "Blocks don't appear in Aspen": make sure you ran INSTALL.bat (or the
    installer) as Administrator - registration writes to the system.
  - "A block fails to load": you likely skipped the Unblock step on a
    downloaded ZIP. Re-run INSTALL.bat; it unblocks the files for you.
  - SmartScreen warning on first run: the build is not code-signed yet;
    choose "More info > Run anyway". A signing certificate removes this.

----------------------------------------------------------------
  بلوكات ون بروسيس - الإصدار 1.0
  عمليات وحدات CAPE-OPEN مخصّصة لـ Aspen Plus V14
  تصميم وتطوير: المهندس نواف - ون بروسيس للمحاكاة

  التثبيت (اختر طريقتك):
   أ) بالمثبّت: شغّل OPBlocks_Setup.exe ووافق على صلاحية المدير.
   ب) بالحزمة المحمولة: انقر يمين على ملف ZIP > خصائص > علّم "إلغاء الحظر" >
      موافق، ثم فك الضغط، ثم انقر نقراً مزدوجاً على INSTALL.bat ووافق على
      صلاحية المدير. للإزالة: UNINSTALL.bat.

  في Aspen: افتح محاكاة، ثم لوحة الموديلات > تبويب CAPE-OPEN، واسحب أي بلوك.
  ملاحظة: إن لم تظهر البلوكات، تأكد أنك شغّلت INSTALL.bat كمسؤول (Administrator).
================================================================

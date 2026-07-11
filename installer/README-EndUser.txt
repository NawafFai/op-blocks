================================================================
  ONE PROCESS Blocks  —  v1.0
  Custom CAPE-OPEN unit operations for Aspen Plus V14 & DWSIM
  Designed & developed by Engineer Nawaf — ONE PROCESS Simulation
================================================================

WHAT THIS IS
  25 process-simulation blocks (desalination, membranes, electrochemical,
  lithium/sorption, energy & gas) that plug into Aspen Plus and DWSIM as
  native CAPE-OPEN unit operations, plus a Manager app to install them.

REQUIREMENTS
  - Windows 10/11 (64-bit)
  - .NET Framework 4.8 (already on Windows 10/11)
  - Aspen Plus V14 and/or DWSIM (to use the blocks)
  The Manager itself is self-contained (no .NET install needed).

INSTALL (setup)
  1. Run  OPBlocks_Setup.exe  and approve the Administrator prompt.
     It registers all 25 blocks in Windows (x64 + x86) automatically.
  2. Open "ONE PROCESS Blocks Manager" from the Start menu / desktop.

USE IN ASPEN PLUS V14
  1. In the Manager, click "Enable in Aspen"  (تفعيل في Aspen).
  2. In Aspen: File > New > User > "ONE PROCESS"  → start your simulation.
     (CAPE-OPEN is already enabled in this template.)
  3. Model Palette > CAPE-OPEN tab → drag any OP-... block onto the flowsheet.
  * If you use your own template instead, enable the library once via
    Customize > Manage Libraries > tick "CAPE-OPEN".

USE IN DWSIM
  Object Palette > Add CAPE-OPEN Unit Operation > pick an OP-... block.

UNINSTALL
  Windows Settings > Apps > "ONE PROCESS Blocks" > Uninstall
  (this unregisters all blocks).

NOTE
  The build is not code-signed yet, so Windows SmartScreen may warn on first
  run — choose "More info > Run anyway". A signing certificate removes this.

----------------------------------------------------------------
  بلوكات ون بروسيس — الإصدار 1.0
  عمليات وحدات CAPE-OPEN مخصّصة لـ Aspen Plus V14 و DWSIM
  تصميم وتطوير: المهندس نواف — ون بروسيس للمحاكاة

  التثبيت: شغّل OPBlocks_Setup.exe ووافق على صلاحية المدير — يسجّل الـ 25 بلوك تلقائياً.
  في Aspen: اضغط "تفعيل في Aspen" من البرنامج، ثم File > New > User > ONE PROCESS،
  وبعدها اسحب أي بلوك من تبويب CAPE-OPEN في لوحة الموديلات.
  في DWSIM: Add CAPE-OPEN Unit Operation واختر البلوك.
  الإزالة: من إعدادات ويندوز > التطبيقات > ONE PROCESS Blocks.
================================================================

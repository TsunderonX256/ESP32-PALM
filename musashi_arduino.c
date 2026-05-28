#include "palm_config.h"

#if PALM_ENABLE_MUSASHI
/*
 * Arduino builds .c/.cpp files in the sketch folder, but it will not normally
 * compile source files from arbitrary nested folders. This wrapper pulls the
 * Musashi core into the sketch build once m68kops.c/h have been generated.
 */
#include "Musashi-master/m68kcpu.c"
#include "Musashi-master/m68kops.c"
#endif

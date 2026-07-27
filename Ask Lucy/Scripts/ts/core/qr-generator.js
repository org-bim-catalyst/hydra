"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var qr_code_styling_1 = require("qr-code-styling");
window.addEventListener("load", function () {
    var uri = document.getElementById("qrCodeData").getAttribute('data-url');
    var qrCode = new qr_code_styling_1.default({
        width: 128,
        height: 128,
        type: "svg",
        data: uri,
        image: "/img/lucy.png",
        dotsOptions: {
            color: "#4267b2",
            type: "rounded"
        },
        backgroundOptions: {
            color: "#e9ebee",
        },
        imageOptions: {
            crossOrigin: "anonymous",
            margin: 5,
            imageSize: 0.7
        }
    });
    qrCode.append(document.getElementById("qrCode"));
    $('#btn-download-qr').on('click', function (events) {
        qrCode.download({ name: "qr", extension: "svg" });
    });
});
//# sourceMappingURL=qr-generator.js.map
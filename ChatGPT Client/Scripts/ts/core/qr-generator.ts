import QRCodeStyling from "qr-code-styling";


window.addEventListener("load", () => {

    const uri = document.getElementById("qrCodeData").getAttribute('data-url');

    const qrCode = new QRCodeStyling({
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
    $('#btn-download-qr').on('click', (events) => {
        qrCode.download({ name: "qr", extension: "svg" });
    });
});


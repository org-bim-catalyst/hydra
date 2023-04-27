export default class ErrorManager {

    logAjaxError(XMLHttpRequest, textStatus, errorThrown) {

        let modal = `<div class="modal fade" id="modal-error-message" tabindex="-1" aria-labelledby="modal-label-error-message" aria-hidden="true" data-target="#staticBackdrop">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content">
                                <div class="modal-header">
                                    <h5 class="modal-title" id="modal-label-error-message">OOOPS something wrong happened!!!</h5>
                                    <button type="button" class="btn-close" data-mdb-dismiss="modal" aria-label="Close"></button>
                                </div>
                                <div class="modal-body">
                                    <div class="col-md-6 mx-auto">
                                        <p class="text-danger" id="p-error-message">
                                                XMLHttpRequest: ${JSON.stringify(XMLHttpRequest)}<br />
                                                Status: ${JSON.stringify(textStatus)}
                                                Error: ${JSON.stringify(errorThrown)}
                                        </p>
                                    </div>
                                </div>

                                <div class="modal-footer">
                                    <button type="button" class="btn btn-sm btn-danger" data-mdb-dismiss="modal" id="button-translate-message">Choose</button>
                                </div>
                            </div>
                        </div>
                    </div>`;

        $(modal).modal('toggle');

    }
}